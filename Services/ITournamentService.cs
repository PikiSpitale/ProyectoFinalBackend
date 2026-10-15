using ProyectAPI.DTOs;

namespace ProyectAPI.Services
{
    public interface ITournamentService
    {
        Task<List<TournamentResponse>> GetAllTournamentsAsync(CancellationToken cancellationToken);
        Task<TournamentResponse?> GetTournamentByIdAsync(string id, CancellationToken cancellationToken);
        Task CreateTournamentAsync(CreateTournamentRequest request, CancellationToken cancellationToken);
        Task<bool> RegisterParticipantAsync(string tournamentId, TournamentParticipantDto participantDto, CancellationToken cancellationToken);
        Task<bool> DeleteTournamentAsync(string id, CancellationToken cancellationToken);

        // Enfrentamientos del torneo (null si el torneo no existe)
        Task<List<MatchResponse>?> GetMatchesAsync(string tournamentId, CancellationToken cancellationToken);

        // Genera las llaves a pedido de un moderador (null si el torneo no existe)
        Task<List<MatchResponse>?> GenerateBracketAsync(string tournamentId, CancellationToken cancellationToken);

        // Genera las llaves de todos los torneos que ya llegaron a su fecha de inicio.
        // Devuelve cuántos torneos se procesaron.
        Task<int> GenerateDueBracketsAsync(CancellationToken cancellationToken);

        // Registra o corrige el resultado de un enfrentamiento y avanza al ganador.
        // Devuelve todas las llaves actualizadas (null si el torneo o el partido no existen).
        Task<List<MatchResponse>?> SetMatchResultAsync(string tournamentId, string matchId, MatchResultRequest request, CancellationToken cancellationToken);

        // Suma o resta un punto a un lado del partido. Si llega al puntaje de victoria del juego, gana solo.
        Task<List<MatchResponse>?> AdjustMatchScoreAsync(string tournamentId, string matchId, MatchScoreRequest request, CancellationToken cancellationToken);

        // Descalifica a un jugador. Si tenía un partido pendiente con rival, el rival gana.
        Task<List<MatchResponse>?> DisqualifyParticipantAsync(string tournamentId, string userId, string? reason, CancellationToken cancellationToken);
    }
}