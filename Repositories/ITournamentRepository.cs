using ProyectAPI.Models;

namespace ProyectAPI.Repositories
{
    public interface ITournamentRepository
    {
        Task<List<Tournament>> GetAllAsync(CancellationToken cancellationToken);
        Task<Tournament?> GetByIdAsync(string id, CancellationToken cancellationToken);
        Task CreateAsync(Tournament tournament, CancellationToken cancellationToken);
        Task<bool> AddParticipantAsync(string tournamentId, TournamentParticipant participant, CancellationToken cancellationToken);
        Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);

        // Cambia el estado solo si el torneo está actualmente en expectedStatus
        Task<bool> TryChangeStatusAsync(string id, string expectedStatus, string newStatus, CancellationToken cancellationToken);

        // Torneos "Próximo" cuya fecha de inicio ya llegó
        Task<List<Tournament>> GetDueForBracketAsync(DateTime utcNow, CancellationToken cancellationToken);

        // Marca a un participante como descalificado
        Task<bool> DisqualifyParticipantAsync(string tournamentId, string userId, string? reason, CancellationToken cancellationToken);
    }
}