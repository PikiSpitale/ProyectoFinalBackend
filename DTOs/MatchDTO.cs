namespace ProyectAPI.DTOs
{
    public record MatchSlotDto(
        string UserId,
        string Username,
        string InGameName
    );

    public record MatchResponse(
        string Id,
        string TournamentId,
        int Round,
        int Position,
        MatchSlotDto? PlayerA,
        MatchSlotDto? PlayerB,
        string? WinnerUserId,
        bool IsBye,
        bool IsWalkover,
        string Status,
        int? ScoreA,
        int? ScoreB
    );

    // Resultado cargado por un moderador.
    // Si WinnerUserId viene vacío, gana el que tenga más puntaje (en empate hay que elegir ganador).
    public record MatchResultRequest(
        int ScoreA,
        int ScoreB,
        string? WinnerUserId
    );

    // Suma o resta un punto (ronda, gol, etc.) a un lado del partido
    // Side: "A" o "B". Delta: 1 o -1.
    public record MatchScoreRequest(
        string Side,
        int Delta
    );
}