using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ProyectAPI.Models
{
    [BsonIgnoreExtraElements]
    public class TeamInvitation
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string TeamId { get; set; } = string.Empty;
        public string TeamName { get; set; } = string.Empty;
        public string TeamTag { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;

        public string InviterId { get; set; } = string.Empty;
        public string InviterUsername { get; set; } = string.Empty;

        public string InviteeId { get; set; } = string.Empty;
        public string InviteeUsername { get; set; } = string.Empty;

        // Pending | Accepted | Rejected
        public string Status { get; set; } = TeamInvitationStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? RespondedAt { get; set; }
    }

    public static class TeamInvitationStatus
    {
        public const string Pending = "Pending";
        public const string Accepted = "Accepted";
        public const string Rejected = "Rejected";
    }
}
