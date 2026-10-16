namespace ProyectAPI.DTOs
{
    // Una estadística que se registra en un juego (ej. kills, créditos)
    public record StatDefinition(
        string Key,
        string Label,
        int Step,
        int? Max
    );

    // Información del juego para armar la pantalla del partido
    public record GameInfoDto(
        string Name,
        string ScoreUnit,
        int? WinScore,
        List<StatDefinition> Stats
    );

    public record MatchDetailResponse(
        string TournamentId,
        string TournamentTitle,
        string TournamentStatus,
        int TotalRounds,
        MatchResponse Match,
        GameInfoDto Game,
        Dictionary<string, int> StatsA,
        Dictionary<string, int> StatsB
    );

    // Fija el valor de una estadística de un jugador. Side: "A" o "B".
    public record MatchStatRequest(
        string Side,
        string Key,
        int Value
    );
}