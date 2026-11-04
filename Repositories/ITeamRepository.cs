using ProyectAPI.Models;

namespace ProyectAPI.Repositories;

public interface ITeamRepository
{
    Task<List<Team>> GetAllAsync(CancellationToken cancellationToken);
    Task<Team?> GetByIdAsync(string id, CancellationToken cancellationToken);
    Task<List<TeamInvitation>> InvitationsAsync(string userOrTeamId, bool received, CancellationToken cancellationToken);
    Task<T> TransactionAsync<T>(Func<ITeamTransaction, CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}

// The service owns business rules; this unit exposes only persisted operations.
public interface ITeamTransaction
{
    Task<Team?> TeamAsync(string id, CancellationToken ct);
    Task<User?> UserAsync(string id, CancellationToken ct);
    Task<bool> HasTeamAsync(string userId, CancellationToken ct);
    Task InsertTeamAsync(Team team, CancellationToken ct);
    Task SaveTeamAsync(Team team, CancellationToken ct);
    Task<TeamInvitation?> InvitationAsync(string id, CancellationToken ct);
    Task InsertInvitationAsync(TeamInvitation invitation, CancellationToken ct);
    Task SaveInvitationAsync(TeamInvitation invitation, CancellationToken ct);
    Task ExpireInvitationsAsync(string teamId, string recipientId, DateTime now, CancellationToken ct);
}
