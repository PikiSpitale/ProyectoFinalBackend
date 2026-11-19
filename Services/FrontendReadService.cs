using ProyectAPI.DTOs;
using ProyectAPI.Repositories;
namespace ProyectAPI.Services;

public sealed class FrontendReadService(FrontendReadRepository repository)
{
    public async Task<MyEnrollmentsPage> Enrollments(string userId, string? status, int page, int pageSize, CancellationToken ct)
    {
        var actor = WalletRepository.UserId(userId);
        if (page < 1 || pageSize < 1 || pageSize > 100 || (long)(page - 1) * pageSize > int.MaxValue) throw new WalletOperationException(400, "invalid_pagination", "Page must be positive; page size must be from 1 to 100.");
        if (status != null) { status = new[] { "Pending", "Admitted", "Cancelled" }.FirstOrDefault(s => s.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase)) ?? throw new WalletOperationException(400, "invalid_status", "Choose Pending, Admitted or Cancelled."); }
        if (!await repository.Active(actor, ct)) throw new WalletOperationException(401, "inactive_user", "The account is not active.");
        return await repository.Enrollments(actor, status, page, pageSize, ct);
    }
}
