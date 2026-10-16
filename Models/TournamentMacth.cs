using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ProyectAPI.Models
{
    // Un enfrentamiento dentro del bracket de un torneo
    [BsonIgnoreExtraElements]
    public class TournamentMatch
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string TournamentId { get; set; } = string.Empty;

        // 1 = primera ronda, la última es la final
        public int Round { get; set; }

        // Posición del partido dentro de su ronda (empieza en 0)
        public int Position { get; set; }

        // Null = lugar vacío (BYE en la primera ronda, "por definir" en las siguientes)
        public MatchSlot? PlayerA { get; set; }
        public MatchSlot? PlayerB { get; set; }

        public string? WinnerUserId { get; set; }
        public int? ScoreA { get; set; }
        public int? ScoreB { get; set; }

        // True si el jugador pasa directo a la siguiente ronda sin rival
        public bool IsBye { get; set; }

        // True si se ganó porque el rival fue descalificado
        public bool IsWalkover { get; set; }
        // Estadísticas de cada jugador en este partido (clave = estadística del juego, ej. "kills")
        public Dictionary<string, int> StatsA { get; set; } = new();
        public Dictionary<string, int> StatsB { get; set; } = new();

        public string Status { get; set; } = MatchStatuses.Pending;
    }

    public class MatchSlot
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string InGameName { get; set; } = string.Empty;
    }

    public static class MatchStatuses
    {
        public const string Pending = "Pendiente";
        public const string Finished = "Finalizado";
    }

    // Estados del torneo (el valor por defecto de Tournament.Status es "Próximo")
    public static class TournamentStatuses
    {
        public const string Upcoming = "Próximo";
        public const string InProgress = "En curso";
        public const string Finished = "Finalizado";
    }
}