using MongoDB.Bson;
using MongoDB.Driver;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;
using ProyectAPI.Services;

namespace ProyectAPI.Repositories;

public sealed class TeamRepository : ITeamRepository
{
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<Team> _teams;
    private readonly IMongoCollection<TeamInvitation> _invitations;
    private readonly Lazy<Task> _indexes;

    public TeamRepository(MongoDbContext context)
    {
        _database = context.Database;
        _teams = _database.GetCollection<Team>("Teams");
        _invitations = _database.GetCollection<TeamInvitation>("TeamInvitations");
        _indexes = new Lazy<Task>(CreateIndexesAsync);
    }

    private async Task CreateIndexesAsync()
    {
        // Fails closed if legacy data already contains membership in multiple teams.
        // No existing documents are deleted or silently repaired.
        await _teams.Indexes.CreateOneAsync(new CreateIndexModel<Team>(
            Builders<Team>.IndexKeys.Ascending("Members.UserId"),
            new CreateIndexOptions<Team> { Name = "teams_member_unique", Unique = true,
                PartialFilterExpression = new BsonDocument("Members.UserId", new BsonDocument("$exists", true)) }));
        await _invitations.Indexes.CreateManyAsync([
            new CreateIndexModel<TeamInvitation>(Builders<TeamInvitation>.IndexKeys
                .Ascending(x => x.TeamId).Ascending(x => x.RecipientId),
                new CreateIndexOptions<TeamInvitation> { Name = "invitation_pending_unique", Unique = true,
                    PartialFilterExpression = Builders<TeamInvitation>.Filter.Eq(x => x.Status, "Pending") }),
            new CreateIndexModel<TeamInvitation>(Builders<TeamInvitation>.IndexKeys
                .Ascending(x => x.RecipientId).Descending(x => x.CreatedAt)),
            new CreateIndexModel<TeamInvitation>(Builders<TeamInvitation>.IndexKeys
                .Ascending(x => x.TeamId).Descending(x => x.CreatedAt))
        ]);
    }

    public Task<List<Team>> GetAllAsync(CancellationToken ct) => _teams.Find(_ => true).ToListAsync(ct);
    public Task<Team?> GetByIdAsync(string id, CancellationToken ct) => _teams.Find(x => x.Id == id).FirstOrDefaultAsync(ct)!;
    public Task<List<TeamInvitation>> InvitationsAsync(string id, bool received, CancellationToken ct) =>
        _invitations.Find(received ? Builders<TeamInvitation>.Filter.Eq(x => x.RecipientId, id)
            : Builders<TeamInvitation>.Filter.Eq(x => x.TeamId, id)).SortByDescending(x => x.CreatedAt).ToListAsync(ct);

    public async Task<T> TransactionAsync<T>(Func<ITeamTransaction, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        await _indexes.Value.WaitAsync(ct);
        using var session = await _database.Client.StartSessionAsync(cancellationToken: ct);
        try
        {
            return await session.WithTransactionAsync((s, token) => action(new Transaction(_database, s), token),
                new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new TeamOperationException(409, "El usuario ya pertenece a un equipo o ya existe una invitación pendiente.");
        }
        catch (MongoCommandException ex) when (ex.Code == 11000)
        {
            throw new TeamOperationException(409, "La operación entra en conflicto con una membresía o invitación existente.");
        }
    }

    private sealed class Transaction(IMongoDatabase database, IClientSessionHandle session) : ITeamTransaction
    {
        private readonly IMongoCollection<Team> _teams = database.GetCollection<Team>("Teams");
        private readonly IMongoCollection<User> _users = database.GetCollection<User>("users");
        private readonly IMongoCollection<TeamInvitation> _invitations = database.GetCollection<TeamInvitation>("TeamInvitations");
        public Task<Team?> TeamAsync(string id, CancellationToken ct) => _teams.Find(session, x => x.Id == id).FirstOrDefaultAsync(ct)!;
        public Task<User?> UserAsync(string id, CancellationToken ct) => _users.Find(session, x => x.Id == id).FirstOrDefaultAsync(ct)!;
        public Task<bool> HasTeamAsync(string userId, CancellationToken ct) => _teams.Find(session,
            Builders<Team>.Filter.ElemMatch(x => x.Members, x => x.UserId == userId)).AnyAsync(ct);
        public Task InsertTeamAsync(Team team, CancellationToken ct) => _teams.InsertOneAsync(session, team, cancellationToken: ct);
        public Task SaveTeamAsync(Team team, CancellationToken ct) => _teams.ReplaceOneAsync(session, x => x.Id == team.Id, team, cancellationToken: ct);
        public Task<TeamInvitation?> InvitationAsync(string id, CancellationToken ct) => _invitations.Find(session, x => x.Id == id).FirstOrDefaultAsync(ct)!;
        public Task InsertInvitationAsync(TeamInvitation invitation, CancellationToken ct) => _invitations.InsertOneAsync(session, invitation, cancellationToken: ct);
        public Task SaveInvitationAsync(TeamInvitation invitation, CancellationToken ct) => _invitations.ReplaceOneAsync(session, x => x.Id == invitation.Id, invitation, cancellationToken: ct);
        public Task ExpireInvitationsAsync(string teamId, string recipientId, DateTime now, CancellationToken ct) =>
            _invitations.UpdateManyAsync(session, x => x.TeamId == teamId && x.RecipientId == recipientId && x.Status == "Pending" && x.ExpiresAt <= now,
                Builders<TeamInvitation>.Update.Set(x => x.Status, "Expired"), cancellationToken: ct);
    }
}
