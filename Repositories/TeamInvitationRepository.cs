using MongoDB.Driver;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;

namespace ProyectAPI.Repositories
{
    public sealed class TeamInvitationRepository : ITeamInvitationRepository
    {
        private readonly IMongoCollection<TeamInvitation> _invitations;

        public TeamInvitationRepository(MongoDbContext mongoDbContext)
        {
            _invitations = mongoDbContext.Database.GetCollection<TeamInvitation>("TeamInvitations");
        }

        public async Task CreateAsync(TeamInvitation invitation, CancellationToken cancellationToken)
        {
            await _invitations.InsertOneAsync(invitation, cancellationToken: cancellationToken);
        }

        public async Task<TeamInvitation?> GetByIdAsync(string id, CancellationToken cancellationToken)
        {
            return await _invitations.Find(i => i.Id == id).FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<List<TeamInvitation>> GetPendingByTeamIdAsync(string teamId, CancellationToken cancellationToken)
        {
            return await _invitations
                .Find(i => i.TeamId == teamId && i.Status == TeamInvitationStatus.Pending)
                .SortByDescending(i => i.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<TeamInvitation>> GetPendingByInviteeIdAsync(string inviteeId, CancellationToken cancellationToken)
        {
            return await _invitations
                .Find(i => i.InviteeId == inviteeId && i.Status == TeamInvitationStatus.Pending)
                .SortByDescending(i => i.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<TeamInvitation?> GetPendingByTeamAndInviteeAsync(string teamId, string inviteeId, CancellationToken cancellationToken)
        {
            return await _invitations
                .Find(i => i.TeamId == teamId && i.InviteeId == inviteeId && i.Status == TeamInvitationStatus.Pending)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<bool> UpdateAsync(TeamInvitation invitation, CancellationToken cancellationToken)
        {
            var result = await _invitations.ReplaceOneAsync(i => i.Id == invitation.Id, invitation, new ReplaceOptions(), cancellationToken);
            return result.ModifiedCount > 0 || result.MatchedCount > 0;
        }
    }
}
