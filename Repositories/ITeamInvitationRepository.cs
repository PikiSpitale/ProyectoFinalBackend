using ProyectAPI.Models;

namespace ProyectAPI.Repositories
{
    public interface ITeamInvitationRepository
    {
        Task CreateAsync(TeamInvitation invitation, CancellationToken cancellationToken);
        Task<TeamInvitation?> GetByIdAsync(string id, CancellationToken cancellationToken);
        Task<List<TeamInvitation>> GetPendingByTeamIdAsync(string teamId, CancellationToken cancellationToken);
        Task<List<TeamInvitation>> GetPendingByInviteeIdAsync(string inviteeId, CancellationToken cancellationToken);
        Task<TeamInvitation?> GetPendingByTeamAndInviteeAsync(string teamId, string inviteeId, CancellationToken cancellationToken);
        Task<bool> UpdateAsync(TeamInvitation invitation, CancellationToken cancellationToken);
    }
}
