using MongoDB.Driver;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;

namespace ProyectAPI.Repositories
{
    public class TeamRepository : ITeamRepository
    {
        private readonly IMongoCollection<Team> _teamsCollection;

        // Usa el MongoDbContext compartido (singleton), igual que TournamentRepository
        public TeamRepository(MongoDbContext mongoDbContext)
        {
            _teamsCollection = mongoDbContext.Database.GetCollection<Team>("Teams");
        }

        // Obtener todos los equipos
        public async Task<List<Team>> GetAllAsync(CancellationToken cancellationToken) =>
            await _teamsCollection.Find(_ => true).ToListAsync(cancellationToken);

        // Obtener equipo por ID
        public async Task<Team?> GetByIdAsync(string id, CancellationToken cancellationToken) =>
            await _teamsCollection.Find(t => t.Id == id).FirstOrDefaultAsync(cancellationToken);

        // Crear un equipo
        public async Task CreateAsync(Team team, CancellationToken cancellationToken) =>
            await _teamsCollection.InsertOneAsync(team, null, cancellationToken);

        // Agregar un miembro al equipo
        public async Task<bool> AddMemberAsync(string teamId, TeamMember member, CancellationToken cancellationToken)
        {
            var filter = Builders<Team>.Filter.Eq(t => t.Id, teamId);
            var update = Builders<Team>.Update.Push(t => t.Members, member);
            var result = await _teamsCollection.UpdateOneAsync(filter, update, null, cancellationToken);
            return result.ModifiedCount > 0;
        }

        // Actualizar el equipo completo (utilizado al abandonar el equipo)
        public async Task<bool> UpdateAsync(Team team, CancellationToken cancellationToken)
        {
            var result = await _teamsCollection.ReplaceOneAsync(t => t.Id == team.Id, team, new ReplaceOptions(), cancellationToken);
            return result.ModifiedCount > 0 || result.MatchedCount > 0;
        }
    }
}