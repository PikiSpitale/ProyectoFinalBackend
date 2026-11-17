using MongoDB.Bson;
using MongoDB.Driver;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;
using ProyectAPI.Services;
namespace ProyectAPI.Repositories;

// All participating effects must use the supplied session, sequentially.
public sealed class WalletRepository
{
    private readonly IMongoDatabase _db;
    private readonly Lazy<Task> _indexes;
    public WalletRepository(MongoDbContext context)
    {
        _db = context.Database;
        _indexes = new Lazy<Task>(InitializeAsync);
    }
    private async Task InitializeAsync()
    {
        await _db.GetCollection<Wallet>("Wallets").Indexes.CreateOneAsync(
            new CreateIndexModel<Wallet>(Builders<Wallet>.IndexKeys.Ascending(x => x.UpdatedAt)));
        await _db.GetCollection<PointTransaction>("PointTransactions").Indexes.CreateOneAsync(
            new CreateIndexModel<PointTransaction>(Builders<PointTransaction>.IndexKeys
                .Ascending(x => x.UserId).Descending(x => x.CreatedAt).Descending(x => x.Id)));
    }
    public static string UserId(string value) => ObjectId.TryParse(value, out var id)
        ? id.ToString() : throw new WalletOperationException(400, "invalid_user", "Identificador no válido.");

    public async Task<T> TransactionAsync<T>(Func<WalletTransaction, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        await _indexes.Value.WaitAsync(ct);
        using var session = await _db.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync((s, token) => action(new WalletTransaction(_db, s), token),
                new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
    }
    public async Task<long> BalanceAsync(string userId, CancellationToken ct)
    {
        var id = UserId(userId);
        var wallet = await _db.GetCollection<Wallet>("Wallets").Find(x => x.UserId == id).FirstOrDefaultAsync(ct);
        return wallet?.Balance ?? 0;
    }
    public async Task<List<PointTransaction>> HistoryAsync(string userId, int page, int pageSize, CancellationToken ct)
    {
        var id = UserId(userId);
        return await _db.GetCollection<PointTransaction>("PointTransactions").Find(x => x.UserId == id)
            .SortByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(checked((page - 1) * pageSize)).Limit(pageSize).ToListAsync(ct);
    }
}

public sealed class WalletTransaction(IMongoDatabase database, IClientSessionHandle session)
{
    // Internal callers may join their domain writes to this transaction; never expose this through HTTP.
    public IClientSessionHandle Session => session;
    private IMongoCollection<Wallet> Wallets => database.GetCollection<Wallet>("Wallets");
    private IMongoCollection<PointTransaction> Entries => database.GetCollection<PointTransaction>("PointTransactions");

    public async Task ProvisionAsync(User user, CancellationToken ct)
    {
        user.Id = WalletRepository.UserId(user.Id);
        await database.GetCollection<User>("users").InsertOneAsync(session, user, cancellationToken: ct);
        await Wallets.InsertOneAsync(session, new Wallet { UserId = user.Id, UpdatedAt = DateTime.UtcNow }, cancellationToken: ct);
        await CreditAsync(user.Id, 1000, "welcome", "account", "welcome", ct);
    }
    public Task<PointTransaction> CreditAsync(string userId, long amount, string reason, string reference, string operationKey, CancellationToken ct)
        => ApplyAsync(userId, Positive(amount), reason, reference, operationKey, null, ct);
    public Task<PointTransaction> DebitAsync(string userId, long amount, string reason, string reference, string operationKey, CancellationToken ct)
        => ApplyAsync(userId, -Positive(amount), reason, reference, operationKey, null, ct);

    public async Task<PointTransaction> RefundAsync(string userId, string debitId, CancellationToken ct)
    {
        var id = WalletRepository.UserId(userId);
        var debit = await Entries.Find(session, x => x.Id == debitId && x.UserId == id).FirstOrDefaultAsync(ct);
        if (debit is null || debit.Amount >= 0)
            throw new WalletOperationException(404, "debit_not_found", "No existe el débito a devolver.");
        return await ApplyAsync(id, checked(-debit.Amount), "refund", debit.Reference, "refund:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(debitId))), debitId, ct);
    }
    private static long Positive(long amount) => amount > 0 ? amount
        : throw new WalletOperationException(400, "invalid_amount", "El importe debe ser positivo.");

    private async Task<PointTransaction> ApplyAsync(string userId, long amount, string reason, string reference,
        string key, string? reversesId, CancellationToken ct)
    {
        var id = WalletRepository.UserId(userId);
        if (string.IsNullOrWhiteSpace(key) || key.Length > 500 || string.IsNullOrWhiteSpace(reason)
            || reason.Length > 100 || string.IsNullOrWhiteSpace(reference) || reference.Length > 200)
            throw new WalletOperationException(400, "invalid_operation", "La operación requiere clave, motivo y referencia válidos.");
        var entryId = id + ":" + key;
        var previous = await Entries.Find(session, x => x.Id == entryId).FirstOrDefaultAsync(ct);
        if (previous is not null)
        {
            if (previous.Amount != amount || previous.Reason != reason || previous.Reference != reference || previous.ReversesId != reversesId)
                throw new WalletOperationException(409, "idempotency_conflict", "La clave ya identifica otra operación.");
            return previous;
        }
        var now = DateTime.UtcNow;
        if (amount > 0)
        {
            // Historical accounts receive no welcome bonus when provisioned by a later domain credit.
            await Wallets.UpdateOneAsync(session, x => x.UserId == id,
                Builders<Wallet>.Update.SetOnInsert(x => x.Balance, 0).SetOnInsert(x => x.UpdatedAt, now),
                new UpdateOptions { IsUpsert = true }, ct);
        }
        var filter = Builders<Wallet>.Filter.Eq(x => x.UserId, id) & (amount < 0
            ? Builders<Wallet>.Filter.Gte(x => x.Balance, -amount)
            : Builders<Wallet>.Filter.Lte(x => x.Balance, long.MaxValue - amount));
        var update = await Wallets.UpdateOneAsync(session, filter,
            Builders<Wallet>.Update.Inc(x => x.Balance, amount).Set(x => x.UpdatedAt, now), cancellationToken: ct);
        if (update.MatchedCount != 1)
            throw new WalletOperationException(409, "balance_unavailable", "Saldo insuficiente o fuera de los límites permitidos.");
        var entry = new PointTransaction { Id = entryId, UserId = id, Amount = amount, Reason = reason,
            Reference = reference, ReversesId = reversesId, CreatedAt = now };
        await Entries.InsertOneAsync(session, entry, cancellationToken: ct);
        return entry;
    }
}
