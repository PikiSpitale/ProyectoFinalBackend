using ProyectAPI.DTOs;

namespace ProyectAPI.Services
{
    public static class GameStatsCatalog
    {
        // StatDefinition(clave, nombre visible, paso de los botones +/-, máximo permitido)
        private static readonly List<StatDefinition> ValorantStats = new()
        {
            new("kills", "Kills", 1, null),
            new("deaths", "Muertes", 1, null),
            new("assists", "Asistencias", 1, null),
            new("credits", "Créditos", 100, 9000)
        };

        private static readonly List<StatDefinition> Cs2Stats = new()
        {
            new("kills", "Kills", 1, null),
            new("deaths", "Muertes", 1, null),
            new("assists", "Asistencias", 1, null),
            new("money", "Dinero", 100, 16000)
        };

        private static readonly List<StatDefinition> LolStats = new()
        {
            new("kills", "Kills", 1, null),
            new("deaths", "Muertes", 1, null),
            new("assists", "Asistencias", 1, null),
            new("cs", "Minions", 1, null),
            new("gold", "Oro", 100, null)
        };

        private static readonly List<StatDefinition> FcStats = new()
        {
            new("shots", "Tiros", 1, null),
            new("shotsOnTarget", "Tiros al arco", 1, null),
            new("possession", "Posesión (%)", 1, 100),
            new("corners", "Córners", 1, null),
            new("fouls", "Faltas", 1, null)
        };

        public static GameInfoDto GetGameInfo(string gameName)
        {
            var stats = gameName switch
            {
                "Valorant" => ValorantStats,
                "Counter-Strike 2" => Cs2Stats,
                "League of Legends" => LolStats,
                "EA FC 27" => FcStats,
                _ => new List<StatDefinition>()
            };

            var unit = gameName switch
            {
                "Valorant" or "Counter-Strike 2" => "Rondas",
                "EA FC 27" => "Goles",
                _ => "Puntos"
            };

            return new GameInfoDto(gameName, unit, GameRules.GetWinScore(gameName), stats);
        }
    }
}