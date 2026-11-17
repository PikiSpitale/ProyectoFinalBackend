using ProyectAPI.Models;
using ProyectAPI.Repositories;
namespace ProyectAPI.Services;

public sealed class WalletService(WalletRepository wallets, IUserRepository users)
{
    private async Task<string> ActiveUserAsync(string userId, CancellationToken ct)
    {
        var id = WalletRepository.UserId(userId);
        if (await users.GetByIdAsync(id, ct) is not { IsActive: true })
            throw new WalletOperationException(401, "inactive_user", "Sesión no válida.");
        return id;
    }
    public async Task<long> BalanceAsync(string userId, CancellationToken ct)
        => await wallets.BalanceAsync(await ActiveUserAsync(userId, ct), ct);
    public async Task<List<PointTransaction>> HistoryAsync(string userId, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new WalletOperationException(400, "invalid_pagination", "Página inválida; tamaño permitido: 1 a 100.");
        return await wallets.HistoryAsync(await ActiveUserAsync(userId, ct), page, pageSize, ct);
    }
}
