public class UserRankingDto
{
    public required string UserId { get; set; }
    public required string Username { get; set; }
    public string? AvatarUrl { get; set; } // Permitimos nulo con '?' por si acaso
    public int Points { get; set; }
    public int TournamentsWon { get; set; }
    public int MatchesPlayed { get; set; }
    public int MatchesWon { get; set; }
    public int MatchesLost { get; set; }
    public double WinRate => MatchesPlayed > 0 ? Math.Round((double)MatchesWon / MatchesPlayed * 100, 1) : 0;
}