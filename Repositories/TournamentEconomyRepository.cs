using MongoDB.Driver;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;
namespace ProyectAPI.Repositories;

// The session flows only within the wallet callback; nested domain calls reuse it.
public sealed class TournamentEconomyRepository(MongoDbContext context)
{
    private static readonly AsyncLocal<IClientSessionHandle?> Current = new();
    public static IClientSessionHandle? Session => Current.Value;
    public IMongoDatabase Database => context.Database;
    public async Task<T> WithSession<T>(IClientSessionHandle session, Func<Task<T>> action)
    {
        var previous = Current.Value;
        Current.Value = session;
        try { return await action(); }
        finally { Current.Value = previous; }
    }
    public IMongoCollection<Tournament> Tournaments => Database.GetCollection<Tournament>("Tournaments");
    public Task<Tournament?> Read(string id, CancellationToken ct) => Tournaments.Find(Session!, x => x.Id == id).FirstOrDefaultAsync(ct)!;
    public Task Save(Tournament value, CancellationToken ct) => Tournaments.ReplaceOneAsync(Session!, x => x.Id == value.Id, value, cancellationToken: ct);
    public async Task<Tournament?> Fence(string id, CancellationToken ct)
    {
        var value = await Tournaments.FindOneAndUpdateAsync(Session!, Builders<Tournament>.Filter.Eq(x => x.Id, id),
            Builders<Tournament>.Update.Inc(x => x.Revision, 1), new FindOneAndUpdateOptions<Tournament> { ReturnDocument = ReturnDocument.After }, ct);
        if (value is { EconomyVersion: 0 })
        {
            if (value.Participants.Count != 0) throw new InvalidOperationException("El torneo histórico requiere limpieza explícita antes de adaptarlo.");
            value.EconomyVersion = 1; value.MaxParticipants = 8;
            value.PrizePool = value.IsFree ? 0 : value.TournamentType == "Equipo" ? 10000 : 2000;
            await Save(value, ct);
        }
        return value;
    }
}
