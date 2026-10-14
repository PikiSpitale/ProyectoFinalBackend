using ProyectAPI.Models;

namespace ProyectAPI.Repositories
{
    public interface ITeamRepository
    {
        Task<List<Team>> GetAllAsync(CancellationToken cancellationToken);
        Task<Team?> GetByIdAsync(string id, CancellationToken cancellationToken);
        Task CreateAsync(Team team, CancellationToken cancellationToken);
        Task<bool> AddMemberAsync(string teamId, TeamMember member, CancellationToken cancellationToken);

        // Firma para actualizar el documento completo del equipo
        Task<bool> UpdateAsync(Team team, CancellationToken cancellationToken);
    }
}