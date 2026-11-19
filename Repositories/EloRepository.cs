using MongoDB.Bson;
using MongoDB.Driver;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;
namespace ProyectAPI.Repositories;

public sealed class EloRepository
{
    private readonly IMongoDatabase _db;
    private readonly Lazy<Task> _indexes;
    public EloRepository(MongoDbContext context) { _db = context.Database; _indexes = new Lazy<Task>(Initialize); }
    private IMongoCollection<EloRating> Ratings(string kind) => _db.GetCollection<EloRating>(kind == "player" ? "PlayerEloRatings" : "TeamEloRatings");
    private IMongoCollection<EloAdjustment> Changes => _db.GetCollection<EloAdjustment>("EloAdjustments");
    private IMongoCollection<BsonDocument> Games => _db.GetCollection<BsonDocument>("EloGames");
    private IMongoCollection<BsonDocument> Settlements => _db.GetCollection<BsonDocument>("EloSettlements");
    private async Task Initialize()
    {
        foreach (var kind in new[] { "player", "team" }) await Ratings(kind).Indexes.CreateManyAsync(new[]{
            new CreateIndexModel<EloRating>(Builders<EloRating>.IndexKeys.Ascending(x=>x.EntityId).Ascending(x=>x.GameName),new CreateIndexOptions{Unique=true,Name="elo_entity_game_unique"}),
            new CreateIndexModel<EloRating>(Builders<EloRating>.IndexKeys.Ascending(x=>x.GameName).Descending(x=>x.Rating).Ascending(x=>x.NameSort).Ascending(x=>x.EntityId),new CreateIndexOptions{Name="elo_leaderboard"})});
        await Changes.Indexes.CreateOneAsync(new CreateIndexModel<EloAdjustment>(Builders<EloAdjustment>.IndexKeys.Ascending(x => x.Kind).Ascending(x => x.EntityId).Ascending(x => x.GameName).Descending(x => x.CreatedAt).Descending(x => x.Id)));
    }
    public Task<Tournament?> Tournament(string id, CancellationToken ct) => _db.GetCollection<Tournament>("Tournaments").Find(x => x.Id == id).FirstOrDefaultAsync(ct)!;
    public async Task Prepare(string game, CancellationToken ct)
    {
        await _indexes.Value.WaitAsync(ct);
        try { await Games.UpdateOneAsync(new BsonDocument("_id", game), new BsonDocument("$setOnInsert", new BsonDocument("Revision", 0L)), new UpdateOptions { IsUpsert = true }, ct); }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey) { /* Concurrent first initialization elected the same game gate. */ }
        catch (MongoCommandException e) when (e.Code == 11000) { /* The winner committed the gate before this transaction. */ }
    }
    public async Task Lock(string game, IClientSessionHandle session, CancellationToken ct)
    {
        var result = await Games.UpdateOneAsync(session, new BsonDocument("_id", game), new BsonDocument("$inc", new BsonDocument("Revision", 1L)), cancellationToken: ct);
        if (result.MatchedCount != 1) throw new InvalidOperationException("Elo game gate was not initialized.");
    }
    public Task<bool> Processed(string tournament, IClientSessionHandle session, CancellationToken ct) => Settlements.Find(session, new BsonDocument("_id", tournament)).AnyAsync(ct);
    public Task Mark(string tournament, string game, IClientSessionHandle session, CancellationToken ct) => Settlements.InsertOneAsync(session, new BsonDocument { { "_id", tournament }, { "GameName", game }, { "RuleVersion", 1 } }, cancellationToken: ct);
    public async Task<EloRating> Rating(string kind, string entity, string name, string game, IClientSessionHandle session, CancellationToken ct)
    {
        var id = game + ":" + entity;
        return await Ratings(kind).Find(session, x => x.Id == id).FirstOrDefaultAsync(ct) ?? new EloRating { Id = id, EntityId = entity, Name = name, NameSort = name.ToLowerInvariant(), Kind = kind, GameName = game };
    }
    public Task Save(EloRating value, IClientSessionHandle session, CancellationToken ct) => Ratings(value.Kind).ReplaceOneAsync(session, x => x.Id == value.Id, value, new ReplaceOptions { IsUpsert = true }, ct);
    public Task Append(EloAdjustment change, IClientSessionHandle session, CancellationToken ct) => Changes.InsertOneAsync(session, change, cancellationToken: ct);
    public Task<List<EloRating>> Rankings(string kind, string? game, int page, int size, CancellationToken ct) => Ratings(kind).Find(game == null ? Builders<EloRating>.Filter.Empty : Builders<EloRating>.Filter.Eq(x => x.GameName, game)).SortByDescending(x => x.Rating).ThenBy(x => x.NameSort).ThenBy(x => x.EntityId).ThenBy(x => x.GameName).Skip(checked((page - 1) * size)).Limit(size).ToListAsync(ct);
    public Task<List<EloAdjustment>> History(string kind, string entity, string game, int page, int size, CancellationToken ct) => Changes.Find(x => x.Kind == kind && x.EntityId == entity && x.GameName == game).SortByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip(checked((page - 1) * size)).Limit(size).ToListAsync(ct);
    public async Task<ProyectAPI.DTOs.CurrentEloDto> Current(string kind, string entity, string game, CancellationToken ct)
    {
        using var session = await _db.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (snapshot, token) =>
        {
            var collection = Ratings(kind);
            var value = await collection.Find(snapshot, x => x.EntityId == entity && x.GameName == game).FirstOrDefaultAsync(token);
            if (value == null) return new ProyectAPI.DTOs.CurrentEloDto(entity, game, kind, null, null, 0, 0, 0);
            var f = Builders<EloRating>.Filter;
            var before = f.Eq(x => x.GameName, game) & (f.Gt(x => x.Rating, value.Rating)
                | (f.Eq(x => x.Rating, value.Rating) & f.Lt(x => x.NameSort, value.NameSort))
                | (f.Eq(x => x.Rating, value.Rating) & f.Eq(x => x.NameSort, value.NameSort) & f.Lt(x => x.EntityId, value.EntityId)));
            var position = await collection.CountDocumentsAsync(snapshot, before, cancellationToken: token) + 1;
            return new ProyectAPI.DTOs.CurrentEloDto(entity, game, kind, value.Rating, position, value.MatchesPlayed, value.MatchesWon, value.MatchesLost);
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot), ct);
    }
}
