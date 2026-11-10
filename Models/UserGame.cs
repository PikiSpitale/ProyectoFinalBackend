namespace ProyectAPI.Models;

public class UserGame
{
    public string GameName { get; set; } = string.Empty;
    public string InGameName { get; set; } = string.Empty;
    public string Rank { get; set; } = string.Empty;

    // Ranking propio de la plataforma: arranca en 0 al vincular el juego y
    // solo suma por torneos jugados en la web (nunca por el rango in-game).
    public int Points { get; set; } = 0;
    public int TournamentsPlayed { get; set; } = 0;
    public int TournamentsWon { get; set; } = 0;
    public int MatchesPlayed { get; set; } = 0;
    public int MatchesWon { get; set; } = 0;
    public int MatchesLost { get; set; } = 0;
}
