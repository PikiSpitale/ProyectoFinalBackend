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
            await GetAllAsync(null, null, null, null, cancellationToken);

        // RF-19: consulta y filtrado de torneos
        public async Task<List<Tournament>> GetAllAsync(string? gameName, string? status, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken)
        {
            var filter = Builders<Tournament>.Filter.Empty;
            if (!string.IsNullOrWhiteSpace(gameName))
                filter &= Builders<Tournament>.Filter.Eq(t => t.GameName, gameName);
            if (!string.IsNullOrWhiteSpace(status))
                filter &= Builders<Tournament>.Filter.Eq(t => t.Status, status);
            if (fromDate.HasValue)
                filter &= Builders<Tournament>.Filter.Gte(t => t.StartDate, fromDate.Value.ToUniversalTime());
            if (toDate.HasValue)
                filter &= Builders<Tournament>.Filter.Lte(t => t.StartDate, toDate.Value.ToUniversalTime());

            return await _tournamentsCollection.Find(filter).SortBy(t => t.StartDate).ToListAsync(cancellationToken);
        }

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

        // RF-17: actualizar torneo
        public async Task<bool> UpdateAsync(Tournament tournament, CancellationToken cancellationToken)
        {
            var result = await _tournamentsCollection.ReplaceOneAsync(t => t.Id == tournament.Id, tournament, new ReplaceOptions(), cancellationToken);
            return result.ModifiedCount > 0 || result.MatchedCount > 0;
        }

        // Eliminar un torneo por su ID
        public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
        {
            var result = await _tournamentsCollection.DeleteOneAsync(t => t.Id == id, cancellationToken);
            return result.DeletedCount > 0;
        }

        public async Task<bool> SetStatusAsync(string id, string newStatus, CancellationToken cancellationToken)
        {
            var update = Builders<Tournament>.Update.Set(t => t.Status, newStatus);
            var result = await _tournamentsCollection.UpdateOneAsync(t => t.Id == id, update, null, cancellationToken);
            return result.ModifiedCount > 0 || result.MatchedCount > 0;
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

        // RF-22: verificación manual de rango
        public async Task<bool> SetParticipantRankVerificationAsync(string tournamentId, string userId, bool verified, string? verifiedBy, CancellationToken cancellationToken)
        {
            var filter = Builders<Tournament>.Filter.And(
                Builders<Tournament>.Filter.Eq(t => t.Id, tournamentId),
                Builders<Tournament>.Filter.ElemMatch(t => t.Participants, p => p.UserId == userId)
            );

            var update = Builders<Tournament>.Update.Combine(
                Builders<Tournament>.Update.Set(t => t.Participants[-1].RankVerified, verified),
                Builders<Tournament>.Update.Set(t => t.Participants[-1].RankVerifiedBy, verifiedBy),
                Builders<Tournament>.Update.Set(t => t.Participants[-1].RankVerifiedAt, verified ? DateTime.UtcNow : null)
            );

            var result = await _tournamentsCollection.UpdateOneAsync(filter, update, null, cancellationToken);
            return result.ModifiedCount > 0;
        }

        // RF-08: bloquea la eliminación del juego si hay un torneo en curso donde el usuario participa
        public async Task<bool> HasInProgressTournamentWithGameAsync(string gameName, string userId, CancellationToken cancellationToken)
        {
            var filter = Builders<Tournament>.Filter.And(
                Builders<Tournament>.Filter.Eq(t => t.Status, TournamentStatuses.InProgress),
                Builders<Tournament>.Filter.Eq(t => t.GameName, gameName),
                Builders<Tournament>.Filter.ElemMatch(t => t.Participants, p => p.UserId == userId)
            );

            return await _tournamentsCollection.Find(filter).AnyAsync(cancellationToken);
        }

        // RF-08: limpia las inscripciones del usuario en torneos que todavía no comenzaron
        public async Task<long> RemoveParticipantFromUpcomingTournamentsAsync(string gameName, string userId, CancellationToken cancellationToken)
        {
            var filter = Builders<Tournament>.Filter.And(
                Builders<Tournament>.Filter.Eq(t => t.Status, TournamentStatuses.Upcoming),
                Builders<Tournament>.Filter.Eq(t => t.GameName, gameName),
                Builders<Tournament>.Filter.ElemMatch(t => t.Participants, p => p.UserId == userId)
            );

            var update = Builders<Tournament>.Update.PullFilter(
                t => t.Participants,
                p => p.UserId == userId
            );

            var result = await _tournamentsCollection.UpdateManyAsync(filter, update, null, cancellationToken);
            return result.ModifiedCount;
        }
    }
}