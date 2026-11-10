namespace ProyectAPI.Services
{
    // Reglas del ranking propio de la web. No dependen del rango in-game.
    public static class RankingPoints
    {
        public const int Participation = 10;
        public const int MatchWin = 25;
        public const int MatchLoss = 10;
        public const int Champion = 100;
        public const int RunnerUp = 50;
        public const int Semifinalist = 25;

        // El bonus de semifinalista solo tiene sentido cuando hubo semifinales.
        public const int MinimumParticipantsForSemifinalist = 4;

        public static string Describe(string type, int? round = null) => type switch
        {
            Models.PointTransactionTypes.Participation => "Participación en el torneo",
            Models.PointTransactionTypes.MatchWin => round.HasValue ? $"Victoria en la ronda {round}" : "Victoria de partido",
            Models.PointTransactionTypes.MatchLoss => round.HasValue ? $"Derrota en la ronda {round}" : "Derrota de partido",
            Models.PointTransactionTypes.Champion => "Campeón del torneo",
            Models.PointTransactionTypes.RunnerUp => "Subcampeón del torneo",
            Models.PointTransactionTypes.Semifinalist => "Semifinalista del torneo",
            _ => "Puntos de ranking"
        };
    }
}
