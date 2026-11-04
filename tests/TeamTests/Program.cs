using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using ProyectAPI.DTOs;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;
using ProyectAPI.Repositories;
using ProyectAPI.Services;

var checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine($"PASS {name}"); }
async Task Error(int expected, Func<Task> action, string name)
{
    try { await action(); throw new Exception($"Expected {expected}: {name}"); }
    catch (TeamOperationException ex) { Check(ex.StatusCode == expected, name); }
}
Check(!new Team().IsPrivate, "legacy defaults public");
Check(!MongoDB.Bson.Serialization.BsonSerializer.Deserialize<Team>(new BsonDocument { { "Name", "legacy" } }).IsPrivate, "stored legacy document defaults public");
Check(new UpdateTeamDto().IsPrivate == null, "omitted privacy update is unchanged");
var invalidDto = new InviteTeamDto { RecipientId = "invalid" };
Check(!Validator.TryValidateObject(invalidDto, new ValidationContext(invalidDto), [], true), "invalid recipient DTO");
var uri = Environment.GetEnvironmentVariable("TEST_MONGODB_URI");
if (string.IsNullOrEmpty(uri))
{
    Console.WriteLine($"{checks} checks passed. SKIP Mongo integration: set TEST_MONGODB_URI to an isolated replica set.");
    return;
}
// This harness never reads application secrets and creates its own disposable database.
if (Environment.GetEnvironmentVariable("TEST_MONGODB_INITIALIZE") == "1")
{
    var direct = new MongoClient("mongodb://127.0.0.1:27187/?directConnection=true");
    try { await direct.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("replSetInitiate", new BsonDocument { { "_id", "tup-tests" }, { "members", new BsonArray { new BsonDocument { { "_id", 0 }, { "host", "127.0.0.1:27187" } } } } })); }
    catch (MongoCommandException ex) when (ex.Code == 23) { }
    for (var i = 0; i < 40; i++)
    {
        var status = await direct.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1));
        if (status.GetValue("isWritablePrimary", false).AsBoolean) break;
        await Task.Delay(250);
    }
}
var client = new MongoClient(uri);
var dbName = "team_tests_" + Guid.NewGuid().ToString("N");
var context = new MongoDbContext(Options.Create(new MongoDbSettings { ConnectionString = uri, DatabaseName = dbName }));
var db = context.Database;
try
{
    var hello = await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1));
    Check(hello.Contains("setName") || hello.GetValue("msg", "") == "isdbgrid", "transaction-capable server");
    var userCollection = db.GetCollection<User>("users");
    var teams = db.GetCollection<Team>("Teams");
    var invites = db.GetCollection<TeamInvitation>("TeamInvitations");
    var repo = new TeamRepository(context);
    var users = new UserRepository(context);
    var clock = new TestClock();
    var service = new TeamService(repo, users, clock);
    async Task<User> User(string name, bool game = true)
    {
        var user = new User { Id = ObjectId.GenerateNewId().ToString(), Username = name, Email = name + "@example.invalid",
            Games = game ? [new UserGame { GameName = "Valorant", InGameName = name, Rank = "Gold" }] : [] };
        await userCollection.InsertOneAsync(user); return user;
    }
    Task<Team> Team(User captain, bool privacy = false) => service.CreateTeamAsync(new TeamCreateDto {
        Name = captain.Username + " team", Tag = "TT", GameName = "Valorant", IsPrivate = privacy }, captain.Id, captain.Username, default);
    var captain = await User("captain"); var captain2 = await User("captain2");
    var recipient = await User("recipient"); var outsider = await User("outsider"); var noGame = await User("nogame", false);
    var privateTeam = await Team(captain, true); var publicTeam = await Team(captain2);
    Check((await service.GetAllTeamsAsync(default)).Count == 2, "public listing includes private teams");
    await Error(403, () => service.JoinTeamAsync(privateTeam.Id, outsider.Id, "", default), "private direct join blocked");
    await Error(403, () => service.UpdateTeamAsync(privateTeam.Id, new UpdateTeamDto { IsPrivate = false }, outsider.Id, default), "noncaptain privacy denied");
    await Error(403, () => service.FindInviteesAsync(privateTeam.Id, outsider.Id, "re", default), "noncaptain search denied");
    await Error(403, () => service.InviteAsync(privateTeam.Id, outsider.Id, recipient.Id, default), "noncaptain invite denied");
    await Error(409, () => service.InviteAsync(privateTeam.Id, captain.Id, captain.Id, default), "self invite denied");
    await Error(409, () => service.InviteAsync(privateTeam.Id, captain.Id, noGame.Id, default), "missing game invite denied");
    await Error(404, () => service.InviteAsync(privateTeam.Id, captain.Id, ObjectId.GenerateNewId().ToString(), default), "missing recipient denied");
    await Error(409, () => service.InviteAsync(privateTeam.Id.ToUpperInvariant(), captain.Id.ToUpperInvariant(), captain.Id.ToUpperInvariant(), default), "uppercase self invitation denied");
    var invitation = await service.InviteAsync(privateTeam.Id.ToUpperInvariant(), captain.Id.ToUpperInvariant(), recipient.Id.ToUpperInvariant(), default);
    Check(invitation.TeamId == privateTeam.Id && invitation.RecipientId == recipient.Id, "invitation identifiers canonicalized");
    Check(invitation.ExpiresAt - invitation.CreatedAt == TimeSpan.FromDays(7), "seven day lifetime");
    await Error(409, () => service.InviteAsync(privateTeam.Id.ToUpperInvariant(), captain.Id, recipient.Id.ToUpperInvariant(), default), "pending duplicate aliases denied");
    await Error(403, () => service.ResolveInvitationAsync(invitation.Id, outsider.Id, "accept", default), "wrong recipient denied");
    await Error(403, () => service.ResolveInvitationAsync(invitation.Id, recipient.Id, "cancel", default), "recipient cannot cancel");
    Check((await service.ListInvitationsAsync(recipient.Id, true, recipient.Id, default)).Count == 1, "received scope");
    Check((await service.ListInvitationsAsync(outsider.Id, true, outsider.Id, default)).Count == 0, "other inbox empty");
    // Deliberate failure after the team write must roll the complete transaction back.
    var failing = new TeamService(new FailRepository(repo), users, clock);
    try { await failing.ResolveInvitationAsync(invitation.Id, recipient.Id, "accept", default); throw new Exception("Expected injected failure"); }
    catch (InjectedFailure) { }
    Check(!(await repo.GetByIdAsync(privateTeam.Id, default))!.Members.Any(x => x.UserId == recipient.Id), "partial failure rolls back membership");
    Check((await invites.Find(x => x.Id == invitation.Id).SingleAsync()).Status == "Pending", "partial failure preserves pending invitation");
    var accepted = await service.ResolveInvitationAsync(invitation.Id.ToUpperInvariant(), recipient.Id.ToUpperInvariant(), "accept", default);
    Check(accepted.Status == "Accepted", "accept commits status");
    await Error(409, () => service.ResolveInvitationAsync(invitation.Id, recipient.Id, "accept", default), "accepted retry denied");
    Check((await repo.GetByIdAsync(privateTeam.Id, default))!.Members.Count(x => x.UserId == recipient.Id) == 1, "accept does not duplicate member");
    await service.UpdateTeamAsync(privateTeam.Id, new UpdateTeamDto { IsPrivate = false }, captain.Id, default);
    Check((await repo.GetByIdAsync(privateTeam.Id, default))!.Members.Count == 2, "privacy change preserves members");
    await service.UpdateTeamAsync(privateTeam.Id, new UpdateTeamDto { IsPrivate = true }, captain.Id, default);
    await Error(409, () => service.InviteAsync(privateTeam.Id, captain.Id, recipient.Id.ToUpperInvariant(), default), "uppercase current member invite denied");
    var reject = await service.InviteAsync(privateTeam.Id, captain.Id, outsider.Id, default);
    await service.ResolveInvitationAsync(reject.Id, outsider.Id, "reject", default);
    await Error(409, () => service.ResolveInvitationAsync(reject.Id, outsider.Id, "accept", default), "rejected cannot reuse");
    var cancel = await service.InviteAsync(privateTeam.Id, captain.Id, outsider.Id, default);
    await service.ResolveInvitationAsync(cancel.Id, captain.Id, "cancel", default);
    await Error(409, () => service.ResolveInvitationAsync(cancel.Id, outsider.Id, "accept", default), "cancelled cannot reuse");
    var expire = await service.InviteAsync(privateTeam.Id, captain.Id, outsider.Id, default);
    clock.UtcNow = clock.UtcNow.AddDays(7);
    Check((await service.ListInvitationsAsync(outsider.Id, true, outsider.Id, default)).Any(x => x.Id == expire.Id && x.Status == "Expired"), "lists derive expiry at exact deadline");
    await Error(410, () => service.ResolveInvitationAsync(expire.Id, outsider.Id, "accept", default), "expired accept denied");
    Check((await invites.Find(x => x.Id == expire.Id).SingleAsync()).Status == "Expired", "expiration persisted despite 410");
    var lazyUser = await User("lazyexpiry");
    var lazyExpired = await service.InviteAsync(privateTeam.Id, captain.Id, lazyUser.Id, default);
    clock.UtcNow = clock.UtcNow.AddDays(7);
    var lazyNew = await service.InviteAsync(privateTeam.Id, captain.Id, lazyUser.Id, default);
    Check(lazyNew.Id != lazyExpired.Id, "reinvite after unprocessed expiry");
    var newInvite = await service.InviteAsync(privateTeam.Id, captain.Id, outsider.Id, default);
    Check(newInvite.Status == "Pending", "can reinvite after expiry");
    await userCollection.UpdateOneAsync(x => x.Id == outsider.Id, Builders<User>.Update.Set(x => x.Games, []));
    await Error(409, () => service.ResolveInvitationAsync(newInvite.Id, outsider.Id, "accept", default), "game revalidated at accept");
    await userCollection.UpdateOneAsync(x => x.Id == outsider.Id, Builders<User>.Update.Set(x => x.Games, recipient.Games));
    var search = await service.FindInviteesAsync(privateTeam.Id, captain.Id, "reci", default);
    Check(search.Any(x => x.Id == recipient.Id), "username substring search");
    Check((await service.FindInviteesAsync(privateTeam.Id, captain.Id, ".*", default)).Count == 0, "search escapes regex");
    // Concurrent accepts of the same event: exactly one succeeds and a single member is inserted.
    async Task<bool> Try(Func<Task> action) { try { await action(); return true; } catch (TeamOperationException ex) when (ex.StatusCode == 409) { return false; } }
    var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Try(() => service.ResolveInvitationAsync(newInvite.Id, outsider.Id, "accept", default))));
    Check(outcomes.Count(x => x) == 1, "concurrent accepts have one winner");
    Check((await repo.GetByIdAsync(privateTeam.Id, default))!.Members.Count(x => x.UserId == outsider.Id) == 1, "concurrent accepts insert once");
    var racer = await User("racer");
    var a = await service.InviteAsync(privateTeam.Id, captain.Id, racer.Id, default);
    var b = await service.InviteAsync(publicTeam.Id, captain2.Id, racer.Id, default);
    var races = await Task.WhenAll(Try(() => service.ResolveInvitationAsync(a.Id, racer.Id, "accept", default)), Try(() => service.ResolveInvitationAsync(b.Id, racer.Id, "accept", default)), Try(() => service.JoinTeamAsync(publicTeam.Id, racer.Id, "", default)));
    Check((await repo.GetAllAsync(default)).Sum(t => t.Members.Count(x => x.UserId == racer.Id)) == 1, "cross-team accepts and join preserve unique membership");
    var joiner = await User("joiner");
    await Task.WhenAll(service.JoinTeamAsync(publicTeam.Id, joiner.Id, "", default), service.UpdateTeamAsync(publicTeam.Id, new UpdateTeamDto { Name = "renamed" }, captain2.Id, default));
    var final = (await repo.GetByIdAsync(publicTeam.Id, default))!;
    Check(final.Name == "renamed" && final.Members.Any(x => x.UserId == joiner.Id), "concurrent edits do not lose joins");
    var creator = await User("creator");
    await Task.WhenAll(Try(() => Team(creator)), Try(() => service.JoinTeamAsync(publicTeam.Id, creator.Id, "", default)));
    Check((await repo.GetAllAsync(default)).Sum(t => t.Members.Count(x => x.UserId == creator.Id)) == 1, "create versus join preserves unique membership");
    Console.WriteLine($"{checks} checks passed using isolated database {dbName}.");
}
finally { await client.DropDatabaseAsync(dbName); }

sealed class TestClock : TimeProvider
{
    public DateTimeOffset UtcNow = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => UtcNow;
}
sealed class InjectedFailure : Exception { }
sealed class FailRepository(ITeamRepository inner) : ITeamRepository
{
    public Task<List<Team>> GetAllAsync(CancellationToken ct) => inner.GetAllAsync(ct);
    public Task<Team?> GetByIdAsync(string id, CancellationToken ct) => inner.GetByIdAsync(id, ct);
    public Task<List<TeamInvitation>> InvitationsAsync(string id, bool received, CancellationToken ct) => inner.InvitationsAsync(id, received, ct);
    public Task<T> TransactionAsync<T>(Func<ITeamTransaction, CancellationToken, Task<T>> action, CancellationToken ct) => inner.TransactionAsync((tx, token) => action(new FailTransaction(tx), token), ct);
}
sealed class FailTransaction(ITeamTransaction inner) : ITeamTransaction
{
    public Task<Team?> TeamAsync(string id, CancellationToken ct) => inner.TeamAsync(id, ct);
    public Task<User?> UserAsync(string id, CancellationToken ct) => inner.UserAsync(id, ct);
    public Task<bool> HasTeamAsync(string id, CancellationToken ct) => inner.HasTeamAsync(id, ct);
    public Task InsertTeamAsync(Team x, CancellationToken ct) => inner.InsertTeamAsync(x, ct);
    public Task SaveTeamAsync(Team x, CancellationToken ct) => inner.SaveTeamAsync(x, ct);
    public Task<TeamInvitation?> InvitationAsync(string id, CancellationToken ct) => inner.InvitationAsync(id, ct);
    public Task InsertInvitationAsync(TeamInvitation x, CancellationToken ct) => inner.InsertInvitationAsync(x, ct);
    public Task SaveInvitationAsync(TeamInvitation x, CancellationToken ct) => throw new InjectedFailure();
    public Task ExpireInvitationsAsync(string teamId, string recipientId, DateTime now, CancellationToken ct) => inner.ExpireInvitationsAsync(teamId, recipientId, now, ct);
}
