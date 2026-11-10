using MongoDB.Driver;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;

namespace ProyectAPI.Repositories
{
    public sealed class PointTransactionRepository : IPointTransactionRepository
    {
        private readonly IMongoCollection<PointTransaction> _transactions;
        private readonly Lazy<Task> _indexesInitialization;

        public PointTransactionRepository(MongoDbContext context)
        {
            _transactions = context.Database.GetCollection<PointTransaction>("PointTransactions");
            _indexesInitialization = new Lazy<Task>(CreateIndexesAsync);
        }

        private Task CreateIndexesAsync()
        {
            return _transactions.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<PointTransaction>(
                    Builders<PointTransaction>.IndexKeys.Ascending(t => t.TournamentId),
                    new CreateIndexOptions { Name = "point_transactions_tournament" }
                ),
                new CreateIndexModel<PointTransaction>(
                    Builders<PointTransaction>.IndexKeys.Ascending(t => t.UserId).Ascending(t => t.GameName),
                    new CreateIndexOptions { Name = "point_transactions_user_game" }
                ),
                new CreateIndexModel<PointTransaction>(
                    Builders<PointTransaction>.IndexKeys.Ascending(t => t.TeamId).Ascending(t => t.GameName),
                    new CreateIndexOptions { Name = "point_transactions_team_game" }
                )
            ]);
        }

        public async Task<List<PointTransaction>> GetAllAsync(CancellationToken cancellationToken)
        {
            await _indexesInitialization.Value.WaitAsync(cancellationToken);
            return await _transactions.Find(_ => true).ToListAsync(cancellationToken);
        }

        public async Task<List<PointTransaction>> GetByTournamentAsync(string tournamentId, CancellationToken cancellationToken) =>
            await _transactions.Find(t => t.TournamentId == tournamentId).ToListAsync(cancellationToken);

        public async Task<List<PointTransaction>> GetByUserAsync(string userId, CancellationToken cancellationToken) =>
            await _transactions.Find(t => t.UserId == userId).ToListAsync(cancellationToken);

        public async Task ReplaceForTournamentAsync(
            string tournamentId,
            IReadOnlyCollection<PointTransaction> transactions,
            CancellationToken cancellationToken)
        {
            await _indexesInitialization.Value.WaitAsync(cancellationToken);

            await _transactions.DeleteManyAsync(t => t.TournamentId == tournamentId, cancellationToken);
            if (transactions.Count > 0)
            {
                await _transactions.InsertManyAsync(transactions, cancellationToken: cancellationToken);
            }
        }

        public async Task DeleteByTournamentAsync(string tournamentId, CancellationToken cancellationToken) =>
            await _transactions.DeleteManyAsync(t => t.TournamentId == tournamentId, cancellationToken);

        public async Task DeleteByUserAndGameAsync(string userId, string gameName, CancellationToken cancellationToken)
        {
            var filter = Builders<PointTransaction>.Filter.And(
                Builders<PointTransaction>.Filter.Eq(t => t.UserId, userId),
                Builders<PointTransaction>.Filter.Eq(t => t.GameName, gameName)
            );
            await _transactions.DeleteManyAsync(filter, cancellationToken);
        }

        // Solo borra el movimiento propio del equipo. Los movimientos individuales
        // de sus integrantes se conservan como historial del jugador.
        public async Task DeleteTeamTransactionsAsync(string teamId, CancellationToken cancellationToken)
        {
            var filter = Builders<PointTransaction>.Filter.And(
                Builders<PointTransaction>.Filter.Eq(t => t.TeamId, teamId),
                Builders<PointTransaction>.Filter.Eq(t => t.UserId, string.Empty)
            );
            await _transactions.DeleteManyAsync(filter, cancellationToken);
        }
    }
}
