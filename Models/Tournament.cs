using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ProyectAPI.Models
{
    public class Tournament
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
        public string BannerUrl { get; set; } = string.Empty;
        public decimal PrizePool { get; set; } = 0;
        public string Status { get; set; } = "Próximo";
        public int MaxParticipants { get; set; } = 32;

        public string Rules { get; set; } = string.Empty;
        public string RankConditions { get; set; } = string.Empty;

        public List<TournamentParticipant> Participants { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // 🌟 Corregido aquí
    }

    public class TournamentParticipant
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string InGameName { get; set; } = string.Empty;
        public string Rank { get; set; } = string.Empty;
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    }
}