public class UserRankingDto
{
    public required string UserId { get; set; }
    public required string Username { get; set; }
    public string? AvatarUrl { get; set; }

    // Juego consultado (null/global = suma de todos los juegos).
    public string? GameName { get; set; }
    public int Points { get; set; }
    public int Position { get; set; }

    // Rango declarado en el juego, se muestra como referencia y no suma puntos.
    public string Rank { get; set; } = string.Empty;
    public string InGameName { get; set; } = string.Empty;

    public int TournamentsPlayed { get; set; }
    public int TournamentsWon { get; set; }
    public int MatchesPlayed { get; set; }
    public int MatchesWon { get; set; }
    public int MatchesLost { get; set; }

    public double WinRate => MatchesPlayed > 0
        ? Math.Round((double)MatchesWon / MatchesPlayed * 100, 1)
        : 0;

    public List<RankingGameDto> Games { get; set; } = new();
}

public class RankingGameDto
{
    public string GameName { get; set; } = string.Empty;
    public int Points { get; set; }
    public string Rank { get; set; } = string.Empty;
    public string InGameName { get; set; } = string.Empty;
    public int TournamentsPlayed { get; set; }
    public int TournamentsWon { get; set; }
    public int MatchesPlayed { get; set; }
    public int MatchesWon { get; set; }
    public int MatchesLost { get; set; }

    public double WinRate => MatchesPlayed > 0
        ? Math.Round((double)MatchesWon / MatchesPlayed * 100, 1)
        : 0;
}

public class TeamRankingDto
{
    public required string TeamId { get; set; }
    public required string Name { get; set; }
    public string Tag { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string CaptainId { get; set; } = string.Empty;
    public int MemberCount { get; set; }

    public int Points { get; set; }
    public int Position { get; set; }
    public int TournamentsPlayed { get; set; }
    public int TournamentsWon { get; set; }
    public int MatchesPlayed { get; set; }
    public int MatchesWon { get; set; }
    public int MatchesLost { get; set; }

    public double WinRate => MatchesPlayed > 0
        ? Math.Round((double)MatchesWon / MatchesPlayed * 100, 1)
        : 0;
}

public class PointTransactionDto
{
    public string Id { get; set; } = string.Empty;
    public string TournamentId { get; set; } = string.Empty;
    public string TournamentTitle { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Points { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? MatchId { get; set; }
    public int? Round { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GameProgressDto
{
    public string GameName { get; set; } = string.Empty;
    public string Rank { get; set; } = string.Empty;
    public string InGameName { get; set; } = string.Empty;
    public int Points { get; set; }
    public int Position { get; set; }
    public int TournamentsPlayed { get; set; }
    public int TournamentsWon { get; set; }
    public int MatchesPlayed { get; set; }
    public int MatchesWon { get; set; }
    public int MatchesLost { get; set; }

    public double WinRate => MatchesPlayed > 0
        ? Math.Round((double)MatchesWon / MatchesPlayed * 100, 1)
        : 0;

    public List<PointTransactionDto> History { get; set; } = new();
}

public class UserProgressDto
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public int TotalPoints { get; set; }
    public List<GameProgressDto> Games { get; set; } = new();
    public List<PointTransactionDto> RecentTransactions { get; set; } = new();
}
