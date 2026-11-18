using MongoDB.Driver;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;
namespace ProyectAPI.Repositories;

public sealed class RewardRepository
{
    private readonly IMongoCollection<RewardRedemption> _receipts;
    private readonly IMongoCollection<User> _users;
    private readonly Lazy<Task> _indexes;
    public RewardRepository(MongoDbContext context)
    {
        _receipts = context.Database.GetCollection<RewardRedemption>("RewardRedemptions");
        _users = context.Database.GetCollection<User>("users");
        _indexes = new Lazy<Task>(() => _receipts.Indexes.CreateManyAsync(new[]
        {
            new CreateIndexModel<RewardRedemption>(Builders<RewardRedemption>.IndexKeys.Ascending(x=>x.UserId).Ascending(x=>x.IdempotencyKey),new CreateIndexOptions{Unique=true,Name="reward_user_key_unique"}),
            new CreateIndexModel<RewardRedemption>(Builders<RewardRedemption>.IndexKeys.Ascending(x=>x.Code),new CreateIndexOptions{Unique=true,Name="reward_demo_code_unique"}),
            new CreateIndexModel<RewardRedemption>(Builders<RewardRedemption>.IndexKeys.Ascending(x=>x.UserId).Descending(x=>x.CreatedAt).Descending(x=>x.Id),new CreateIndexOptions{Name="reward_user_history"})
        }));
    }
    public Task Initialize(CancellationToken ct) => _indexes.Value.WaitAsync(ct);
    public Task<bool> Active(string id, IClientSessionHandle session, CancellationToken ct) => _users.Find(session, x => x.Id == id && x.IsActive).AnyAsync(ct);
    public Task<bool> Active(string id, CancellationToken ct) => _users.Find(x => x.Id == id && x.IsActive).AnyAsync(ct);
    public Task<RewardRedemption?> Find(string id, IClientSessionHandle session, CancellationToken ct) => _receipts.Find(session, x => x.Id == id).FirstOrDefaultAsync(ct)!;
    public Task Insert(RewardRedemption receipt, IClientSessionHandle session, CancellationToken ct) => _receipts.InsertOneAsync(session, receipt, cancellationToken: ct);
    public Task<List<RewardRedemption>> History(string user, int page, int size, CancellationToken ct) => _receipts.Find(x => x.UserId == user).SortByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip(checked((page - 1) * size)).Limit(size).ToListAsync(ct);
}
