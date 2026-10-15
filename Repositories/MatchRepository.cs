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
                .Find(m => m.TournamentId == tournamentId)
                .SortBy(m => m.Round).ThenBy(m => m.Position)
                .ToListAsync(cancellationToken);

        public async Task InsertManyAsync(IEnumerable<TournamentMatch> matches, CancellationToken cancellationToken)
        {
            var list = matches.ToList();
            if (list.Count == 0) return;

            await _matchesCollection.InsertManyAsync(list, null, cancellationToken);
        }

        public async Task DeleteByTournamentAsync(string tournamentId, CancellationToken cancellationToken) =>
            await _matchesCollection.DeleteManyAsync(m => m.TournamentId == tournamentId, cancellationToken);

        public async Task UpdateAsync(TournamentMatch match, CancellationToken cancellationToken) =>
            await _matchesCollection.ReplaceOneAsync(m => m.Id == match.Id, match, new ReplaceOptions(), cancellationToken);
    }
}