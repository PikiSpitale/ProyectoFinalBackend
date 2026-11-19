using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using ProyectAPI.DTOs;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;
namespace ProyectAPI.Repositories;

public sealed class FrontendReadRepository(MongoDbContext context)
{
    private readonly Lazy<Task> _index = new(() => context.Database.GetCollection<Tournament>("Tournaments").Indexes.CreateOneAsync(
        new CreateIndexModel<Tournament>(Builders<Tournament>.IndexKeys.Ascending("Enrollments.Players.UserId").Descending(x => x.CreatedAt).Descending(x => x.Id), new CreateIndexOptions { Name = "tournament_enrollment_inbox" })));
    public Task<bool> Active(string id, CancellationToken ct) => context.Database.GetCollection<User>("users").Find(x => x.Id == id && x.IsActive).AnyAsync(ct);
    public async Task<MyEnrollmentsPage> Enrollments(string actor, string? status, int page, int size, CancellationToken ct)
    {
        await _index.Value.WaitAsync(ct);
        // Filter again after unwind so another request in a matching tournament is never disclosed.
        var match = new BsonDocument("Enrollments.Players.UserId", actor);
        if (status != null) match.Add("Enrollments.Status", status);
        var pipeline = new[] {
            new BsonDocument("$match", new BsonDocument("Enrollments.Players.UserId", actor)),
            new BsonDocument("$unwind", "$Enrollments"), new BsonDocument("$match", match),
            new BsonDocument("$sort", new BsonDocument{{"CreatedAt",-1},{"_id",-1},{"Enrollments.Id",1}}),
            new BsonDocument("$skip", (long)(page-1)*size), new BsonDocument("$limit", size+1),
            new BsonDocument("$project", new BsonDocument{{"Title",1},{"GameName",1},{"Status",1},{"StartDate",1},{"ClosedAt",1},{"IsFree",1},{"EntryPoints",1},{"Enrollments",1}})
        };
        var documents = await context.Database.GetCollection<BsonDocument>("Tournaments").Aggregate<BsonDocument>(pipeline).ToListAsync(ct);
        var items = documents.Take(size).Select(d =>
        {
            var e = BsonSerializer.Deserialize<TournamentEnrollment>(d["Enrollments"].AsBsonDocument);
            // Deserialize the projected tournament without the unwound enrollment field.
            var header = d.DeepClone().AsBsonDocument; header.Remove("Enrollments");
            var t = BsonSerializer.Deserialize<Tournament>(header);
            return new MyEnrollmentDto(t.Id, t.Title, t.GameName, t.Status, t.StartDate, t.ClosedAt, t.IsFree, t.IsFree ? 0 : t.EntryPoints, e.Id, e.TeamId, e.TeamName, e.Status, e.Players.Select(p => new EnrollmentPlayerDto(p.UserId, p.Username, p.Confirmed)).ToList());
        }).ToList();
        return new(page, size, documents.Count > size, items);
    }
}
