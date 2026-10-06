namespace ProyectAPI.Models;

public class UserStats
{
    public int MatchesPlayed { get; set; } = 0;
    public int Wins { get; set; } = 0;
    public int Losses { get; set; } = 0;
    public int Draws { get; set; } = 0;
    public double Winrate { get; set; } = 0.0; // Lo calcularemos automáticamente
}