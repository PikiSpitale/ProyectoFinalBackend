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

        // Agregar un participante al torneo.
        // Solo se agrega si el torneo sigue abierto ("Próximo") y el usuario no estaba inscrito.
        public async Task<bool> AddParticipantAsync(string tournamentId, TournamentParticipant participant, CancellationToken cancellationToken)
        {
            var filter = Builders<Tournament>.Filter.And(
                Builders<Tournament>.Filter.Eq(t => t.Id, tournamentId),
                Builders<Tournament>.Filter.Eq(t => t.Status, TournamentStatuses.Upcoming),
                Builders<Tournament>.Filter.Not(
                    Builders<Tournament>.Filter.ElemMatch(t => t.Participants, p => p.UserId == participant.UserId)
                )
            );
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

        // Cambia el estado de forma atómica: solo si el estado actual coincide con el esperado
        public async Task<bool> TryChangeStatusAsync(string id, string expectedStatus, string newStatus, CancellationToken cancellationToken)
        {
            var filter = Builders<Tournament>.Filter.And(
                Builders<Tournament>.Filter.Eq(t => t.Id, id),
                Builders<Tournament>.Filter.Eq(t => t.Status, expectedStatus)
            );
            var update = Builders<Tournament>.Update.Set(t => t.Status, newStatus);
            var result = await _tournamentsCollection.UpdateOneAsync(filter, update, null, cancellationToken);
            return result.ModifiedCount > 0;
        }

        // Torneos que todavía no empezaron pero cuya fecha de inicio ya pasó
        public async Task<List<Tournament>> GetDueForBracketAsync(DateTime utcNow, CancellationToken cancellationToken) =>
            await _tournamentsCollection
                .Find(t => t.Status == TournamentStatuses.Upcoming && t.StartDate <= utcNow)
                .ToListAsync(cancellationToken);

        // Marca como descalificado al participante (solo ese elemento del arreglo)
        public async Task<bool> DisqualifyParticipantAsync(string tournamentId, string userId, string? reason, CancellationToken cancellationToken)
        {
            var filter = Builders<Tournament>.Filter.And(
                Builders<Tournament>.Filter.Eq(t => t.Id, tournamentId),
                Builders<Tournament>.Filter.ElemMatch(t => t.Participants, p => p.UserId == userId)
            );

            // [-1] es el operador posicional de Mongo: el elemento que coincidió con el filtro
            var update = Builders<Tournament>.Update.Combine(
                Builders<Tournament>.Update.Set(t => t.Participants[-1].IsDisqualified, true),
                Builders<Tournament>.Update.Set(t => t.Participants[-1].DisqualificationReason, reason)
            );

            var result = await _tournamentsCollection.UpdateOneAsync(filter, update, null, cancellationToken);
            return result.ModifiedCount > 0;
        }
    }
}