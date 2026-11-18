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
        string TournamentType = "Individual",
        bool IsFree = false
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
        List<TournamentParticipantDto> Participants,
        bool IsFree = false, long EntryPoints = 250, long ChampionPoints = 1000,
        long RunnerUpPoints = 600, long SemifinalPoints = 200, DateTime? ClosedAt = null
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