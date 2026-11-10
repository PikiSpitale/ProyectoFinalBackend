using ProyectAPI.Models;

namespace ProyectAPI.Repositories
{
    public interface ITournamentRepository
    {
        Task<List<Tournament>> GetAllAsync(CancellationToken cancellationToken);
        Task<List<Tournament>> GetAllAsync(string? gameName, string? status, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken);
        Task<Tournament?> GetByIdAsync(string id, CancellationToken cancellationToken);
        Task CreateAsync(Tournament tournament, CancellationToken cancellationToken);
        Task<bool> UpdateAsync(Tournament tournament, CancellationToken cancellationToken);
        Task<bool> AddParticipantAsync(string tournamentId, TournamentParticipant participant, CancellationToken cancellationToken);
        Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);

        // Cambia el estado solo si el torneo está actualmente en expectedStatus
        Task<bool> TryChangeStatusAsync(string id, string expectedStatus, string newStatus, CancellationToken cancellationToken);
        Task<bool> SetStatusAsync(string id, string newStatus, CancellationToken cancellationToken);

        // Torneos "Próximo" cuya fecha de inicio ya llegó
        Task<List<Tournament>> GetDueForBracketAsync(DateTime utcNow, CancellationToken cancellationToken);

        // Marca a un participante como descalificado
        Task<bool> DisqualifyParticipantAsync(string tournamentId, string userId, string? reason, CancellationToken cancellationToken);

        // RF-22: verificación manual de rango
        Task<bool> SetParticipantRankVerificationAsync(string tournamentId, string userId, bool verified, string? verifiedBy, CancellationToken cancellationToken);

        // RF-08: ¿el usuario tiene una inscripción en un torneo "En curso" de ese juego?
        Task<bool> HasInProgressTournamentWithGameAsync(string gameName, string userId, CancellationToken cancellationToken);

        // RF-08: al desvincular un juego, se retiran las inscripciones a torneos "Próximo" de ese juego
        Task<long> RemoveParticipantFromUpcomingTournamentsAsync(string gameName, string userId, CancellationToken cancellationToken);

        // Un equipo no puede modificar su plantilla ni eliminarse mientras tenga un torneo en curso.
        Task<bool> HasInProgressTournamentWithTeamAsync(string teamId, CancellationToken cancellationToken);

        // Al eliminar un equipo se retiran sus inscripciones a torneos que todavía no comenzaron.
        Task<long> RemoveTeamFromUpcomingTournamentsAsync(string teamId, CancellationToken cancellationToken);

        // Mantiene actualizada la plantilla congelada de las inscripciones a torneos "Próximo".
        Task<long> SyncTeamRosterInUpcomingTournamentsAsync(string teamId, string teamName, List<TournamentRosterMember> roster, CancellationToken cancellationToken);
    }
}