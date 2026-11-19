namespace ProyectAPI.DTOs;

public sealed record CurrentEloDto(string EntityId, string GameName, string Kind, int? Rating, long? Position, int MatchesPlayed, int MatchesWon, int MatchesLost);
public sealed record EnrollmentPlayerDto(string UserId, string Username, bool Confirmed);
public sealed record MyEnrollmentDto(string TournamentId, string Title, string GameName, string TournamentStatus, DateTime StartDate, DateTime? ClosedAt, bool IsFree, long EntryPoints, string RequestId, string? TeamId, string? TeamName, string Status, List<EnrollmentPlayerDto> Players);
public sealed record MyEnrollmentsPage(int Page, int PageSize, bool HasMore, List<MyEnrollmentDto> Items);
