namespace ProyectAPI.DTOs
{
    public record CreateTournamentRequest(
        string Title,
        string GameName,
        string BannerUrl,
        decimal PrizePool,
        int MaxParticipants,
        string Rules,
        string RankConditions,
        DateTime StartDate,
        DateTime EndDate,
        string TournamentType = "Individual"
    );

    public record TournamentResponse(
        string Id,
        string Title,
        string GameName,
        string BannerUrl,
        decimal PrizePool,
        string Status,
        int MaxParticipants,
        string TournamentType,
        string Rules,
        string RankConditions,
        DateTime StartDate,
        DateTime EndDate,
        List<TournamentParticipantDto> Participants
    );

    public record TournamentParticipantDto(
        string UserId,
        string Username,
        string InGameName,
        string Rank,
        DateTime RegisteredAt,
        bool IsDisqualified = false,
        string? DisqualificationReason = null,
        string? TeamId = null,
        string? TeamName = null
    );

    public record TeamTournamentRegisterRequest(
        string TeamId
    );

    public record DisqualifyRequest(
        string? Reason
    );
}