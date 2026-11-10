using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ProyectAPI.Models
{
    [BsonIgnoreExtraElements]
    public class Team
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Tag { get; set; } = string.Empty;

        public string LogoUrl { get; set; } = string.Empty;

        public string GameName { get; set; } = string.Empty;

        public string CaptainId { get; set; } = string.Empty;

        // InviteOnly (RF-11) | Open (unión pública con filtro opcional). Vacío = legado -> Open.
        public string JoinPolicy { get; set; } = string.Empty;
        // None | Fixed | Minimum (aplica cuando JoinPolicy = Open)
        public string RankFilterType { get; set; } = TeamRankFilterType.None;
        // Rango fijo o rango mínimo (según RankFilterType)
        public string? RankFilterValue { get; set; }

        // Ranking propio de la plataforma para torneos por equipo.
        public int Points { get; set; } = 0;
        public int TournamentsPlayed { get; set; } = 0;
        public int TournamentsWon { get; set; } = 0;
        public int MatchesPlayed { get; set; } = 0;
        public int MatchesWon { get; set; } = 0;
        public int MatchesLost { get; set; } = 0;

        public List<TeamMember> Members { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public static class TeamJoinPolicy
    {
        public const string InviteOnly = "InviteOnly";
        public const string Open = "Open";
    }

    public static class TeamRankFilterType
    {
        public const string None = "None";
        public const string Fixed = "Fixed";
        public const string Minimum = "Minimum";
    }
}