namespace ProyectAPI.Models;

public class UserStats
{
    public int MatchesPlayed { get; set; } = 0;
    public int Wins { get; set; } = 0;
    public int Losses { get; set; } = 0;
    public int Draws { get; set; } = 0;
    public double Winrate { get; set; } = 0.0;
    public int Points { get; set; } = 0;
    public int TournamentsWon { get; set; } = 0;
    public int MatchesWon { get; set; } = 0;
    public int MatchesLost { get; set; } = 0;
}