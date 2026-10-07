using MongoDB.Driver;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;

namespace ProyectAPI.Repositories
{
    public sealed class TournamentRepository : ITournamentRepository
    {
        private readonly IMongoCollection<Tournament> _tournamentsCollection;

        public TournamentRepository(MongoDbContext mongoDbContext)
        {
            // Ajusta "Database" si en tu MongoDbContext la propiedad se llama de otra manera (ej: _database o GetDatabase())
            var database = mongoDbContext.Database;
            _tournamentsCollection = database.GetCollection<Tournament>("Tournaments");
        }

        public async Task<List<Tournament>> GetAllAsync(CancellationToken cancellationToken) =>
            await _tournamentsCollection.Find(_ => true).ToListAsync(cancellationToken);

        public async Task<Tournament?> GetByIdAsync(string id, CancellationToken cancellationToken) =>
            await _tournamentsCollection.Find(t => t.Id == id).FirstOrDefaultAsync(cancellationToken);

        public async Task CreateAsync(Tournament tournament, CancellationToken cancellationToken) =>
            await _tournamentsCollection.InsertOneAsync(tournament, null, cancellationToken);

        public async Task<bool> AddParticipantAsync(string tournamentId, TournamentParticipant participant, CancellationToken cancellationToken)
        {
            var filter = Builders<Tournament>.Filter.Eq(t => t.Id, tournamentId);
            var update = Builders<Tournament>.Update.Push(t => t.Participants, participant);
            var result = await _tournamentsCollection.UpdateOneAsync(filter, update, null, cancellationToken);
            return result.ModifiedCount > 0;
        }
    }
}