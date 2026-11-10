using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ProyectAPI.Models
{
    // Movimiento del ranking web. Es la fuente de verdad: los puntos cacheados
    // en UserGame.Points y Team.Points se reconstruyen a partir de estos registros.
    [BsonIgnoreExtraElements]
    public class PointTransaction
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        // Jugador que recibe los puntos. Vacío cuando el movimiento es solo del equipo.
        public string UserId { get; set; } = string.Empty;

        // Equipo asociado (en torneos por equipo también se guarda en los movimientos de sus miembros).
        public string? TeamId { get; set; }

        public string GameName { get; set; } = string.Empty;
        public string TournamentId { get; set; } = string.Empty;
        public string TournamentTitle { get; set; } = string.Empty;

        // Participation | MatchWin | MatchLoss | Champion | RunnerUp | Semifinalist
        public string Type { get; set; } = string.Empty;
        public int Points { get; set; }
        public string Description { get; set; } = string.Empty;

        public string? MatchId { get; set; }
        public int? Round { get; set; }

        // Fecha del evento dentro del torneo (no la fecha del último recálculo).
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public static class PointTransactionTypes
    {
        public const string Participation = "Participation";
        public const string MatchWin = "MatchWin";
        public const string MatchLoss = "MatchLoss";
        public const string Champion = "Champion";
        public const string RunnerUp = "RunnerUp";
        public const string Semifinalist = "Semifinalist";
    }
}
