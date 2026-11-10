using ProyectAPI.Models;

namespace ProyectAPI.Repositories
{
    public interface IPointTransactionRepository
    {
        Task<List<PointTransaction>> GetAllAsync(CancellationToken cancellationToken);
        Task<List<PointTransaction>> GetByTournamentAsync(string tournamentId, CancellationToken cancellationToken);
        Task<List<PointTransaction>> GetByUserAsync(string userId, CancellationToken cancellationToken);

        // Reconstrucción idempotente: reemplaza todos los movimientos de un torneo.
        Task ReplaceForTournamentAsync(string tournamentId, IReadOnlyCollection<PointTransaction> transactions, CancellationToken cancellationToken);

        Task DeleteByTournamentAsync(string tournamentId, CancellationToken cancellationToken);
        Task DeleteByUserAndGameAsync(string userId, string gameName, CancellationToken cancellationToken);
        Task DeleteTeamTransactionsAsync(string teamId, CancellationToken cancellationToken);
    }
}
