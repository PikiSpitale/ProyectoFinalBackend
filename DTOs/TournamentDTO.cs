namespace ProyectAPI.DTOs
{
    public record CreateTournamentRequest(
        string Title,
        string GameName,
        string BannerUrl,
        decimal PrizePool,
        int MaxParticipants,
        string Rules,
        string RankConditions
    );

    public record TournamentResponse(
        string Id,
        string Title,
        string GameName,
        string BannerUrl,
        decimal PrizePool,
        string Status,
        int MaxParticipants,
        string Rules,
        string RankConditions,
        List<TournamentParticipantDto> Participants
    );

    public record TournamentParticipantDto(
        string UserId,
        string Username,
        string InGameName,
        string Rank,
        DateTime RegisteredAt
    );
}