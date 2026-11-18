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
        public int MaxParticipants { get; set; } = 8;
        public int EconomyVersion { get; set; }
        public bool IsFree { get; set; }
        public long EntryPoints { get; set; } = 250;
        public long ChampionPoints { get; set; } = 1000;
        public long RunnerUpPoints { get; set; } = 600;
        public long SemifinalPoints { get; set; } = 200;
        public long Revision { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? ClosedBy { get; set; }
        public List<TournamentMatch> ClosedResults { get; set; } = new();
        public List<TournamentEnrollment> Enrollments { get; set; } = new();

        public string TournamentType { get; set; } = "Individual"; // Individual o Equipo

        public string Rules { get; set; } = string.Empty;
        public string RankConditions { get; set; } = string.Empty;

        // Nuevas propiedades de fechas para el torneo
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public List<TournamentParticipant> Participants { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TournamentParticipant
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string InGameName { get; set; } = string.Empty;
        public string Rank { get; set; } = string.Empty;
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

        // Un moderador puede descalificar a un jugador que no cumple las condiciones
        public bool IsDisqualified { get; set; } = false;
        public string? DisqualificationReason { get; set; }

        public string? TeamId { get; set; }
        public string? TeamName { get; set; }
    }
}