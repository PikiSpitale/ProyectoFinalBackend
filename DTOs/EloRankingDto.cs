namespace ProyectAPI.DTOs;

public sealed record EloRankingDto(string EntityId, string Name, string GameName, string Kind, int Rating, int MatchesPlayed, int MatchesWon, int MatchesLost);
