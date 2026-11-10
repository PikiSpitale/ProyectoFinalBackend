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
        int? StartersCount = null,
        int? SubstitutesCount = null
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
        int StartersCount = 0,
        int SubstitutesCount = 0,
        string? CreatedByUsername = null
    );

    // El UserId se sobrescribe con el del token; los demás campos se completan
    // desde el perfil del usuario. Se dejan opcionales para clientes mínimos.
    public record TournamentParticipantDto(
        string UserId = "",
        string Username = "",
        string InGameName = "",
        string Rank = "",
        DateTime RegisteredAt = default,
        bool IsDisqualified = false,
        string? DisqualificationReason = null,
        bool RankVerified = false,
        string? RankVerifiedBy = null,
        DateTime? RankVerifiedAt = null,
        string? TeamId = null,
        string? TeamName = null
    );

    public record TeamTournamentRegisterRequest(
        string TeamId
    );

    public record DisqualifyRequest(
        string? Reason
    );

    // RF-17: actualización de torneo por moderador
    public record UpdateTournamentRequest(
        string? Title,
        string? GameName,
        string? BannerUrl,
        decimal? PrizePool,
        int? MaxParticipants,
        string? Rules,
        string? RankConditions,
        DateTime? StartDate,
        DateTime? EndDate,
        string? TournamentType,
        int? StartersCount = null,
        int? SubstitutesCount = null
    );

    // RF-22: verificación manual de rango
    public record VerifyRankRequest(
        bool Verified,
        string? Note
    );
}