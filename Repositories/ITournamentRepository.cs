using ProyectAPI.Models;

namespace ProyectAPI.Repositories
{
    public interface ITournamentRepository
    {
        Task<List<Tournament>> GetAllAsync(CancellationToken cancellationToken);
        Task<Tournament?> GetByIdAsync(string id, CancellationToken cancellationToken);
        Task CreateAsync(Tournament tournament, CancellationToken cancellationToken);
        Task<bool> AddParticipantAsync(string tournamentId, TournamentParticipant participant, CancellationToken cancellationToken);
    }
}