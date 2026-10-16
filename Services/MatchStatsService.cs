using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Repositories;

namespace ProyectAPI.Services
{
    public sealed class MatchStatsService : IMatchStatsService
    {
        private readonly ITournamentRepository _tournamentRepository;
        private readonly IMatchRepository _matchRepository;

        public MatchStatsService(ITournamentRepository tournamentRepository, IMatchRepository matchRepository)
        {
            _tournamentRepository = tournamentRepository;
            _matchRepository = matchRepository;
        }

        public async Task<MatchDetailResponse?> GetMatchDetailAsync(string tournamentId, string matchId, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentRepository.GetByIdAsync(tournamentId, cancellationToken);
            if (tournament is null) return null;

            var matches = await _matchRepository.GetByTournamentAsync(tournamentId, cancellationToken);
            var match = matches.FirstOrDefault(m => m.Id == matchId);
            if (match is null) return null;

            return BuildDetail(tournament, matches, match);
        }

        public async Task<MatchDetailResponse?> UpdateStatAsync(string tournamentId, string matchId, MatchStatRequest request, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentRepository.GetByIdAsync(tournamentId, cancellationToken);
            if (tournament is null) return null;

            var matches = await _matchRepository.GetByTournamentAsync(tournamentId, cancellationToken);
            var match = matches.FirstOrDefault(m => m.Id == matchId);
            if (match is null) return null;

            if (tournament.Status != TournamentStatuses.InProgress && tournament.Status != TournamentStatuses.Finished)
            {
                throw new InvalidOperationException("El torneo no está en juego.");
            }

            if (match.IsBye || match.PlayerA is null || match.PlayerB is null)
            {
                throw new InvalidOperationException("Este partido todavía no tiene definidos a los dos rivales.");
            }

            var game = GameStatsCatalog.GetGameInfo(tournament.GameName);
            var definition = game.Stats.FirstOrDefault(s => s.Key == request.Key);
            if (definition is null)
            {
                throw new InvalidOperationException("Esa estadística no existe para este juego.");
            }

            var side = request.Side?.Trim().ToUpperInvariant();
            if (side != "A" && side != "B")
            {
                throw new InvalidOperationException("El lado del partido debe ser A o B.");
            }

            if (request.Value < 0)
            {
                throw new InvalidOperationException("El valor no puede ser negativo.");
            }

            if (definition.Max is not null && request.Value > definition.Max)
            {
                throw new InvalidOperationException($"El máximo para «{definition.Label}» es {definition.Max}.");
            }

            match.StatsA ??= new Dictionary<string, int>();
            match.StatsB ??= new Dictionary<string, int>();

            var stats = side == "A" ? match.StatsA : match.StatsB;
            stats[definition.Key] = request.Value;

            await _matchRepository.UpdateAsync(match, cancellationToken);

            return BuildDetail(tournament, matches, match);
        }

        private static MatchDetailResponse BuildDetail(Tournament tournament, List<TournamentMatch> matches, TournamentMatch match)
        {
            var game = GameStatsCatalog.GetGameInfo(tournament.GameName);

            return new MatchDetailResponse(
                tournament.Id,
                tournament.Title,
                tournament.Status,
                matches.Max(m => m.Round),
                MapMatch(match),
                game,
                FillStats(game, match.StatsA),
                FillStats(game, match.StatsB)
            );
        }

        // Devuelve todas las estadísticas del juego, con 0 en las que todavía no se cargaron
        private static Dictionary<string, int> FillStats(GameInfoDto game, Dictionary<string, int>? stats) =>
            game.Stats.ToDictionary(
                s => s.Key,
                s => stats is not null && stats.TryGetValue(s.Key, out var value) ? value : 0
            );

        private static MatchResponse MapMatch(TournamentMatch m) =>
            new(
                m.Id,
                m.TournamentId,
                m.Round,
                m.Position,
                MapSlot(m.PlayerA),
                MapSlot(m.PlayerB),
                m.WinnerUserId,
                m.IsBye,
                m.IsWalkover,
                m.Status,
                m.ScoreA,
                m.ScoreB
            );

        private static MatchSlotDto? MapSlot(MatchSlot? slot) =>
            slot is null ? null : new MatchSlotDto(slot.UserId, slot.Username, slot.InGameName);
    }
}