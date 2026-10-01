using MongoDB.Driver;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;

namespace ProyectAPI.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly IMongoCollection<User> _users;
    private readonly Lazy<Task> _indexesInitialization;

    public UserRepository(MongoDbContext context)
    {
        _users = context.Database.GetCollection<User>("users");
        _indexesInitialization = new Lazy<Task>(CreateIndexesAsync);
    }

    private Task CreateIndexesAsync()
    {
        return _users.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(user => user.Email),
                new CreateIndexOptions
                {
                    Name = "users_email_unique",
                    Unique = true
                }
            ),
            new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(user => user.Username),
                new CreateIndexOptions
                {
                    Name = "users_username_unique",
                    Unique = true
                }
            )
        ]);
    }

    public Task<bool> ExistsByEmailAsync(
        string email,
        CancellationToken cancellationToken
    )
    {
        return _users.Find(user => user.Email == email).AnyAsync(cancellationToken);
    }

    public Task<bool> ExistsByUsernameAsync(
        string username,
        CancellationToken cancellationToken
    )
    {
        return _users.Find(user => user.Username == username).AnyAsync(cancellationToken);
    }

    public async Task<User> CreateAsync(
        User user,
        CancellationToken cancellationToken
    )
    {
        await _indexesInitialization.Value.WaitAsync(cancellationToken);
        await _users.InsertOneAsync(user, cancellationToken: cancellationToken);
        return user;
    }

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        return _users.Find(user => user.Email == email).FirstOrDefaultAsync(cancellationToken);
    }

    // --- NUEVOS MÉTODOS PARA EL PERFIL ---

    public Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        return _users.Find(user => user.Id == id).FirstOrDefaultAsync(cancellationToken);
    }

    public Task UpdateAsync(User user, CancellationToken cancellationToken)
    {
        return _users.ReplaceOneAsync(u => u.Id == user.Id, user, cancellationToken: cancellationToken);
    }
}