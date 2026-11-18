using MongoDB.Driver;
using ProyectAPI.Models;
using ProyectAPI.Repositories;
namespace ProyectAPI.Services;

public sealed class EloService(EloRepository repository, TimeProvider clock)
{
    // Indexes and the first game gate must exist before the closure transaction begins.
    public async Task Prepare(string tournamentId, CancellationToken ct)
    {
        var t = await repository.Tournament(WalletRepository.UserId(tournamentId), ct);
        if (t != null && t.ClosedAt == null) await repository.Prepare(EloCalculator.Game(t.GameName), ct);
    }
    public async Task Process(Tournament t, List<TournamentMatch> matches, IClientSessionHandle session, CancellationToken ct)
    {
        if (t.ClosedAt != null) return;
        var game = EloCalculator.Game(t.GameName);
        await repository.Lock(game, session, ct);
        if (await repository.Processed(t.Id, session, ct)) return;
        var entrants = t.Enrollments.Where(x => x.Status == "Admitted").ToDictionary(x => x.Players[0].UserId);
        foreach (var match in matches.OrderBy(x => x.Round).ThenBy(x => x.Position))
        {
            if (match.IsBye || match.IsWalkover) continue;
            if (match.Status != MatchStatuses.Finished || match.PlayerA == null || match.PlayerB == null || match.WinnerUserId == null) throw new InvalidOperationException("An Elo match requires two rivals and a winner.");
            var a = entrants[match.PlayerA.UserId]; var b = entrants[match.PlayerB.UserId]; var aWins = match.WinnerUserId == match.PlayerA.UserId;
            var left = new List<EloRating>(); var right = new List<EloRating>();
            foreach (var p in a.Players) left.Add(await repository.Rating("player", p.UserId, p.Username, game, session, ct));
            foreach (var p in b.Players) right.Add(await repository.Rating("player", p.UserId, p.Username, game, session, ct));
            // Snapshot every opponent before any participant is adjusted.
            var leftAverage = left.Average(x => x.Rating); var rightAverage = right.Average(x => x.Rating);
            if (t.TournamentType == "Equipo")
            {
                var teamA = await repository.Rating("team", a.TeamId!, a.TeamName ?? a.TeamId!, game, session, ct);
                var teamB = await repository.Rating("team", b.TeamId!, b.TeamName ?? b.TeamId!, game, session, ct);
                var beforeA = teamA.Rating; var beforeB = teamB.Rating;
                await Adjust(teamA, beforeB, new[] { teamB.EntityId }, aWins, t, match, session, ct);
                await Adjust(teamB, beforeA, new[] { teamA.EntityId }, !aWins, t, match, session, ct);
            }
            foreach (var p in left) await Adjust(p, rightAverage, right.Select(x => x.EntityId), aWins, t, match, session, ct);
            foreach (var p in right) await Adjust(p, leftAverage, left.Select(x => x.EntityId), !aWins, t, match, session, ct);
        }
        await repository.Mark(t.Id, game, session, ct);
        t.EloRuleVersion = EloCalculator.Version;
    }
    private async Task Adjust(EloRating rating, double opponent, IEnumerable<string> opponentIds, bool won, Tournament t, TournamentMatch match, IClientSessionHandle session, CancellationToken ct)
    {
        var before = rating.Rating; rating.Rating = EloCalculator.Next(before, opponent, won);
        rating.MatchesPlayed++; if (won) rating.MatchesWon++; else rating.MatchesLost++;
        await repository.Save(rating, session, ct);
        await repository.Append(new EloAdjustment { Id = t.Id + ":" + match.Id + ":" + rating.Kind + ":" + rating.EntityId, EntityId = rating.EntityId, Kind = rating.Kind, GameName = rating.GameName, TournamentId = t.Id, MatchId = match.Id, Round = match.Round, Position = match.Position, Before = before, After = rating.Rating, OpponentRating = opponent, OpponentEntityIds = opponentIds.ToList(), ExpectedScore = EloCalculator.Expected(before, opponent), Result = won ? 1 : 0, CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds()).UtcDateTime }, session, ct);
    }
}
