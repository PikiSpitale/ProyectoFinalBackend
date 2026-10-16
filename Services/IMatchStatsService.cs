using ProyectAPI.DTOs;

namespace ProyectAPI.Services
{
    public interface IMatchStatsService
    {
        // Detalle público de un partido con sus estadísticas (null si no existe)
        Task<MatchDetailResponse?> GetMatchDetailAsync(string tournamentId, string matchId, CancellationToken cancellationToken);

        // Fija el valor de una estadística de un jugador (solo moderadores, lo valida el controller)
        Task<MatchDetailResponse?> UpdateStatAsync(string tournamentId, string matchId, MatchStatRequest request, CancellationToken cancellationToken);
    }
}