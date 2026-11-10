using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;

// Utilidad de pruebas sobre la base de desarrollo: lista/promueve/elimina datos.
// La conexión se toma de variables de entorno, de user secrets o de appsettings,
// en ese orden. No requiere una ruta absoluta al proyecto.
static string? FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "appsettings.json"))) return dir.FullName;
        dir = dir.Parent;
    }
    return null;
}

static List<Dictionary<string, JsonElement>> LoadUserSecrets()
{
    var result = new List<Dictionary<string, JsonElement>>();
    var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "UserSecrets");
    if (!Directory.Exists(root)) return result;

    foreach (var file in Directory.EnumerateFiles(root, "secrets.json", SearchOption.AllDirectories))
    {
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(file));
            if (data is not null) result.Add(data);
        }
        catch (JsonException)
        {
            // Ignora archivos de otros proyectos que no sean JSON válidos.
        }
    }

    // Los secretos del proyecto correcto son los que definen la conexión.
    return result
        .OrderByDescending(s => s.ContainsKey("MongoDbSettings:ConnectionString")
            || (s.TryGetValue("MongoDbSettings", out var m) && m.ValueKind == JsonValueKind.Object
                && m.TryGetProperty("ConnectionString", out _)))
        .ToList();
}

var backend = Environment.GetEnvironmentVariable("PROYECT_BACKEND")
    ?? FindRepoRoot(AppContext.BaseDirectory)
    ?? Directory.GetCurrentDirectory();
var settingsPath = Path.Combine(backend, "appsettings.json");
var json = File.Exists(settingsPath)
    ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(settingsPath))!
    : new Dictionary<string, JsonElement>();
var secretsList = LoadUserSecrets();

string Config(string section, string prop)
{
    var env = Environment.GetEnvironmentVariable($"{section}__{prop}");
    if (!string.IsNullOrWhiteSpace(env)) return env;

    foreach (var secrets in secretsList)
    {
        if (secrets.TryGetValue($"{section}:{prop}", out var sf) && sf.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(sf.GetString())) return sf.GetString()!;
        if (secrets.TryGetValue(section, out var sn) && sn.ValueKind == JsonValueKind.Object
            && sn.TryGetProperty(prop, out var sp) && sp.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(sp.GetString())) return sp.GetString()!;
    }

    if (json.TryGetValue(section, out var jn) && jn.ValueKind == JsonValueKind.Object
        && jn.TryGetProperty(prop, out var jp) && jp.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(jp.GetString())) return jp.GetString()!;
    return "";
}

var cs = Config("MongoDbSettings", "ConnectionString");
if (string.IsNullOrWhiteSpace(cs))
{
    Console.Error.WriteLine("No se encontró MongoDbSettings:ConnectionString. Configurala en user secrets o en la variable MongoDbSettings__ConnectionString.");
    return 1;
}

var dbName = Config("MongoDbSettings", "DatabaseName");
if (string.IsNullOrWhiteSpace(dbName)) dbName = "ProyectoFinal";

var client = new MongoClient(cs);
var db = client.GetDatabase(dbName);

var users = db.GetCollection<BsonDocument>("users");
var tournaments = db.GetCollection<BsonDocument>("Tournaments");
var matches = db.GetCollection<BsonDocument>("Matches");
var pointTransactions = db.GetCollection<BsonDocument>("PointTransactions");

string Arg(int i) => args.Length > i ? args[i] : "";

switch (Arg(0))
{
    case "list":
        foreach (var u in users.Find(FilterDefinition<BsonDocument>.Empty).ToList())
        {
            var games = u.TryGetValue("Games", out var g) && g.IsBsonArray ? g.AsBsonArray.Count : 0;
            Console.WriteLine($"{u.GetValue("Username").AsString}\t{u.GetValue("Role").AsString}\tgames={games}\t{u.GetValue("_id")}");
        }
        break;

    case "promote":
    case "demote":
    {
        var username = Arg(1);
        var role = Arg(0) == "promote" ? "Moderator" : "User";
        var res = users.UpdateOne(Builders<BsonDocument>.Filter.Eq("Username", username),
            Builders<BsonDocument>.Update.Set("Role", role));
        Console.WriteLine($"modified={res.ModifiedCount} user={username} role={role}");
        break;
    }

    case "delete-user":
    {
        var username = Arg(1);
        var user = users.Find(Builders<BsonDocument>.Filter.Eq("Username", username)).FirstOrDefault();
        if (user is null) { Console.WriteLine("not-found"); break; }
        var id = user.GetValue("_id");
        var idStr = id.BsonType == BsonType.String ? id.AsString : id.AsObjectId.ToString();
        users.DeleteOne(Builders<BsonDocument>.Filter.Eq("_id", id));
        // Limpieza de datos asociados de prueba
        tournaments.UpdateMany(FilterDefinition<BsonDocument>.Empty,
            Builders<BsonDocument>.Update.Pull("Participants", new BsonDocument("UserId", idStr)));
        db.GetCollection<BsonDocument>("Teams").UpdateMany(FilterDefinition<BsonDocument>.Empty,
            Builders<BsonDocument>.Update.Pull("Members", new BsonDocument("UserId", idStr)));
        db.GetCollection<BsonDocument>("TeamInvitations").DeleteMany(
            Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("InviterId", idStr),
                Builders<BsonDocument>.Filter.Eq("InviteeId", idStr)));
        pointTransactions.DeleteMany(Builders<BsonDocument>.Filter.Eq("UserId", idStr));
        Console.WriteLine($"deleted-user={username}");
        break;
    }

    case "delete-tournament":
    {
        var tid = Arg(1);
        tournaments.DeleteOne(Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(tid)));
        matches.DeleteMany(Builders<BsonDocument>.Filter.Eq("TournamentId", tid));
        pointTransactions.DeleteMany(Builders<BsonDocument>.Filter.Eq("TournamentId", tid));
        Console.WriteLine($"deleted-tournament={tid}");
        break;
    }

    // Mueve la fecha de inicio de un torneo (para probar el cierre de la inscripción).
    case "set-start":
    {
        var tid = Arg(1);
        var minutes = double.Parse(Arg(2), System.Globalization.CultureInfo.InvariantCulture);
        var start = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(minutes), DateTimeKind.Utc);
        var res = tournaments.UpdateOne(
            Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(tid)),
            Builders<BsonDocument>.Update.Set("StartDate", new BsonDateTime(start)));
        Console.WriteLine($"modified={res.ModifiedCount} tournament={tid} start={start:o}");
        break;
    }

    case "delete-team":
    {
        var name = Arg(1);
        var teams = db.GetCollection<BsonDocument>("Teams");
        var teamDocs = teams.Find(Builders<BsonDocument>.Filter.Eq("Name", name)).ToList();
        foreach (var teamDoc in teamDocs)
        {
            var teamId = teamDoc.GetValue("_id");
            var teamIdStr = teamId.BsonType == BsonType.String ? teamId.AsString : teamId.AsObjectId.ToString();
            pointTransactions.DeleteMany(Builders<BsonDocument>.Filter.Eq("TeamId", teamIdStr));
        }
        var res = teams.DeleteMany(Builders<BsonDocument>.Filter.Eq("Name", name));
        Console.WriteLine($"deleted-teams={res.DeletedCount} name={name}");
        break;
    }

    default:
        Console.WriteLine("uso: list | promote <user> | demote <user> | delete-user <user> | delete-tournament <id> | delete-team <name> | set-start <id> <minutos>");
        break;
}

return 0;
