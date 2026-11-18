using MongoDB.Driver;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;

namespace ProyectAPI.Repositories
{
    public sealed class MatchRepository : IMatchRepository
    {
        private readonly IMongoCollection<TournamentMatch> _matchesCollection;

        public MatchRepository(MongoDbContext mongoDbContext)
        {
            _matchesCollection = mongoDbContext.Database.GetCollection<TournamentMatch>("Matches");
        }

        // Enfrentamientos de un torneo ordenados por ronda y posición
        public async Task<List<TournamentMatch>> GetByTournamentAsync(string tournamentId, CancellationToken cancellationToken) =>
            await _matchesCollection
                .ScopedFind(m => m.TournamentId == tournamentId)
                .SortBy(m => m.Round).ThenBy(m => m.Position)
                .ToListAsync(cancellationToken);

        public async Task InsertManyAsync(IEnumerable<TournamentMatch> matches, CancellationToken cancellationToken)
        {
            var list = matches.ToList();
            if (list.Count == 0) return;

            await _matchesCollection.ScopedInsertManyAsync(list, null, cancellationToken);
        }

        public async Task DeleteByTournamentAsync(string tournamentId, CancellationToken cancellationToken) =>
            await _matchesCollection.ScopedDeleteManyAsync(m => m.TournamentId == tournamentId, cancellationToken);

        public async Task UpdateAsync(TournamentMatch match, CancellationToken cancellationToken) =>
            await _matchesCollection.ScopedReplaceOneAsync(m => m.Id == match.Id, match, new ReplaceOptions(), cancellationToken);
    }
}