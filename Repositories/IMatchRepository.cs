using ProyectAPI.Models;

namespace ProyectAPI.Repositories
{
    public interface IMatchRepository
    {
        Task<List<TournamentMatch>> GetByTournamentAsync(string tournamentId, CancellationToken cancellationToken);
        Task InsertManyAsync(IEnumerable<TournamentMatch> matches, CancellationToken cancellationToken);
        Task DeleteByTournamentAsync(string tournamentId, CancellationToken cancellationToken);

        // Reemplaza el documento completo del enfrentamiento
        Task UpdateAsync(TournamentMatch match, CancellationToken cancellationToken);
    }
}