using ProyectAPI.Models;

namespace ProyectAPI.Repositories;

public interface IUserRepository
{
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken);
    Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken);
    Task<User> CreateAsync(User user, CancellationToken cancellationToken);
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    // NUEVOS:
    Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken);
    Task UpdateAsync(User user, CancellationToken cancellationToken);
}