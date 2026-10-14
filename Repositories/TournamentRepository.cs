using MongoDB.Driver;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;

namespace ProyectAPI.Repositories
{
    public sealed class TournamentRepository : ITournamentRepository
    {
        private readonly IMongoCollection<Tournament> _tournamentsCollection;

        // Constructor del repositorio de torneos
        public TournamentRepository(MongoDbContext mongoDbContext)
        {
            var database = mongoDbContext.Database;
            _tournamentsCollection = database.GetCollection<Tournament>("Tournaments");
        }

        // Obtener lista completa de torneos
        public async Task<List<Tournament>> GetAllAsync(CancellationToken cancellationToken) =>
            await _tournamentsCollection.Find(_ => true).ToListAsync(cancellationToken);

        // Obtener un torneo por su ID
        public async Task<Tournament?> GetByIdAsync(string id, CancellationToken cancellationToken) =>
            await _tournamentsCollection.Find(t => t.Id == id).FirstOrDefaultAsync(cancellationToken);

        // Crear un nuevo torneo incluyendo validación de fechas (fin no anterior a inicio)
        public async Task CreateAsync(Tournament tournament, CancellationToken cancellationToken)
        {
            if (tournament.EndDate.Date < tournament.StartDate.Date)
            {
                throw new InvalidOperationException("La fecha de finalización no puede ser anterior a la fecha de inicio del torneo.");
            }

            await _tournamentsCollection.InsertOneAsync(tournament, null, cancellationToken);
        }

        // Agregar un participante al torneo
        public async Task<bool> AddParticipantAsync(string tournamentId, TournamentParticipant participant, CancellationToken cancellationToken)
        {
            var filter = Builders<Tournament>.Filter.Eq(t => t.Id, tournamentId);
            var update = Builders<Tournament>.Update.Push(t => t.Participants, participant);
            var result = await _tournamentsCollection.UpdateOneAsync(filter, update, null, cancellationToken);
            return result.ModifiedCount > 0;
        }

        // Eliminar un torneo por su ID
        public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
        {
            var result = await _tournamentsCollection.DeleteOneAsync(t => t.Id == id, cancellationToken);
            return result.DeletedCount > 0;
        }
    }
}