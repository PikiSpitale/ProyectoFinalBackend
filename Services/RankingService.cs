using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Repositories;
namespace ProyectAPI.Services;

public sealed class RankingService(EloRepository repository)
{
    private static void Page(int page, int size)
    {
        if (page < 1 || size < 1 || size > 100 || (long)(page - 1) * size > int.MaxValue) throw new WalletOperationException(400, "invalid_pagination", "Page must be positive and page size must be from 1 to 100.");
    }
    public async Task<List<EloRankingDto>> Rankings(string kind, string? gameName, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); var game = EloCalculator.Game(gameName);
        return (await repository.Rankings(kind, game, page, pageSize, ct)).Select(x => new EloRankingDto(x.EntityId, x.Name, x.GameName, x.Kind, x.Rating, x.MatchesPlayed, x.MatchesWon, x.MatchesLost)).ToList();
    }
    public Task<List<EloAdjustment>> History(string kind, string entityId, string? gameName, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); var game = EloCalculator.Game(gameName); var entity = WalletRepository.UserId(entityId);
        return repository.History(kind, entity, game, page, pageSize, ct);
    }
    public Task<CurrentEloDto> Current(string kind, string entityId, string? gameName, CancellationToken ct)
        => repository.Current(kind, WalletRepository.UserId(entityId), EloCalculator.Game(gameName), ct);
    // Compatibility endpoint: same array/fields; Points is now a deprecated alias for per-game Elo.
    public async Task<IEnumerable<UserRankingDto>> GetLeaderboardAsync(string? gameName, CancellationToken ct)
    {
        var game = gameName == null ? null : EloCalculator.Game(gameName);
        return (await repository.Rankings("player", game, 1, 50, ct)).Select(x => new UserRankingDto { UserId = x.EntityId, Username = x.Name, Points = x.Rating, Rating = x.Rating, GameName = x.GameName, Kind = x.Kind, MatchesPlayed = x.MatchesPlayed, MatchesWon = x.MatchesWon, MatchesLost = x.MatchesLost }).ToList();
    }
}
