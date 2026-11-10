using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Repositories;

namespace ProyectAPI.Services
{
    public sealed class RankingService
    {
        private readonly IUserRepository _userRepository;
        private readonly ITeamRepository _teamRepository;
        private readonly ITournamentRepository _tournamentRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly IPointTransactionRepository _transactionRepository;

        public RankingService(
            IUserRepository userRepository,
            ITeamRepository teamRepository,
            ITournamentRepository tournamentRepository,
            IMatchRepository matchRepository,
            IPointTransactionRepository transactionRepository)
        {
            _userRepository = userRepository;
            _teamRepository = teamRepository;
            _tournamentRepository = tournamentRepository;
            _matchRepository = matchRepository;
            _transactionRepository = transactionRepository;
        }

        // RF-31 / RF-34: ranking de jugadores por videojuego (global = suma de juegos).
        public async Task<List<UserRankingDto>> GetLeaderboardAsync(string? gameName, CancellationToken cancellationToken)
        {
            var filter = NormalizeGame(gameName);
            var users = await _userRepository.GetAllAsync(cancellationToken);
            var rows = new List<UserRankingDto>();

            foreach (var user in users)
            {
                if (!user.IsActive) continue;

                var games = (user.Games ?? new List<UserGame>())
                    .Where(g => filter is null || IsSameGame(g.GameName, filter))
                    .ToList();

                // Solo participan del ranking quienes tienen al menos un juego
                // vinculado (el ranking global tampoco incluye usuarios vacíos).
                if (games.Count == 0) continue;

                var gameDtos = games.Select(g => new RankingGameDto
                {
                    GameName = g.GameName,
                    Points = g.Points,
                    Rank = g.Rank,
                    InGameName = g.InGameName,
                    TournamentsPlayed = g.TournamentsPlayed,
                    TournamentsWon = g.TournamentsWon,
                    MatchesPlayed = g.MatchesPlayed,
                    MatchesWon = g.MatchesWon,
                    MatchesLost = g.MatchesLost
                }).ToList();

                var primary = games.OrderByDescending(g => g.Points).FirstOrDefault();
                var selectedGame = filter is null ? null : games.FirstOrDefault();

                rows.Add(new UserRankingDto
                {
                    UserId = user.Id,
                    Username = string.IsNullOrWhiteSpace(user.Username) ? "Usuario" : user.Username,
                    AvatarUrl = user.AvatarUrl,
                    GameName = filter,
                    Points = gameDtos.Sum(g => g.Points),
                    Rank = selectedGame?.Rank ?? primary?.Rank ?? string.Empty,
                    InGameName = selectedGame?.InGameName ?? primary?.InGameName ?? string.Empty,
                    TournamentsPlayed = gameDtos.Sum(g => g.TournamentsPlayed),
                    TournamentsWon = gameDtos.Sum(g => g.TournamentsWon),
                    MatchesPlayed = gameDtos.Sum(g => g.MatchesPlayed),
                    MatchesWon = gameDtos.Sum(g => g.MatchesWon),
                    MatchesLost = gameDtos.Sum(g => g.MatchesLost),
                    Games = gameDtos
                });
            }

            rows = rows
                .OrderByDescending(r => r.Points)
                .ThenByDescending(r => r.TournamentsWon)
                .ThenByDescending(r => r.MatchesWon)
                .ThenBy(r => r.Username, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.UserId, StringComparer.Ordinal)
                .ToList();

            for (var i = 0; i < rows.Count; i++) rows[i].Position = i + 1;
            return rows;
        }

        // RF-32 / RF-34: ranking de equipos por videojuego.
        public async Task<List<TeamRankingDto>> GetTeamLeaderboardAsync(string? gameName, CancellationToken cancellationToken)
        {
            var filter = NormalizeGame(gameName);
            var teams = await _teamRepository.GetAllAsync(cancellationToken);

            var rows = teams
                .Where(t => filter is null || IsSameGame(t.GameName, filter))
                .Select(t => new TeamRankingDto
                {
                    TeamId = t.Id,
                    Name = t.Name,
                    Tag = t.Tag,
                    LogoUrl = t.LogoUrl,
                    GameName = t.GameName,
                    CaptainId = t.CaptainId,
                    MemberCount = t.Members?.Count ?? 0,
                    Points = t.Points,
                    TournamentsPlayed = t.TournamentsPlayed,
                    TournamentsWon = t.TournamentsWon,
                    MatchesPlayed = t.MatchesPlayed,
                    MatchesWon = t.MatchesWon,
                    MatchesLost = t.MatchesLost
                })
                .OrderByDescending(t => t.Points)
                .ThenByDescending(t => t.TournamentsWon)
                .ThenByDescending(t => t.MatchesWon)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < rows.Count; i++) rows[i].Position = i + 1;
            return rows;
        }

        // RF-40: progreso e historial del jugador desde que empezó en la web.
        public async Task<UserProgressDto?> GetUserProgressAsync(string userId, CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user is null) return null;

            var games = (user.Games ?? new List<UserGame>()).ToList();
            var transactions = await _transactionRepository.GetByUserAsync(userId, cancellationToken);
            var allUsers = await _userRepository.GetAllAsync(cancellationToken);

            var progress = new List<GameProgressDto>();
            foreach (var game in games)
            {
                var gameTransactions = transactions
                    .Where(t => IsSameGame(t.GameName, game.GameName))
                    .OrderByDescending(t => t.CreatedAt)
                    .ThenByDescending(t => t.Points)
                    .Select(MapTransaction)
                    .ToList();

                progress.Add(new GameProgressDto
                {
                    GameName = game.GameName,
                    Rank = game.Rank,
                    InGameName = game.InGameName,
                    Points = game.Points,
                    Position = CalculatePosition(allUsers, userId, game.GameName),
                    TournamentsPlayed = game.TournamentsPlayed,
                    TournamentsWon = game.TournamentsWon,
                    MatchesPlayed = game.MatchesPlayed,
                    MatchesWon = game.MatchesWon,
                    MatchesLost = game.MatchesLost,
                    History = gameTransactions
                });
            }

            return new UserProgressDto
            {
                UserId = user.Id,
                Username = string.IsNullOrWhiteSpace(user.Username) ? "Usuario" : user.Username,
                AvatarUrl = user.AvatarUrl,
                TotalPoints = games.Sum(g => g.Points),
                Games = progress.OrderByDescending(g => g.Points).ToList(),
                RecentTransactions = transactions
                    .OrderByDescending(t => t.CreatedAt)
                    .ThenByDescending(t => t.Points)
                    .Take(100)
                    .Select(MapTransaction)
                    .ToList()
            };
        }

        // Reconstruye el ranking de un torneo (idempotente). Se llama al cambiar
        // resultados, avanzar, descalificar, cancelar o eliminar un torneo.
        public async Task RebuildTournamentAsync(string tournamentId, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentRepository.GetByIdAsync(tournamentId, cancellationToken);
            if (tournament is null)
            {
                await _transactionRepository.DeleteByTournamentAsync(tournamentId, cancellationToken);
                await RefreshAllCachesAsync(cancellationToken);
                return;
            }

            await RebuildTournamentCoreAsync(tournament, cancellationToken);
            await RefreshAllCachesAsync(cancellationToken);
        }

        // Backfill al iniciar: torneos ya jugados en la web sin movimientos y
        // limpieza de movimientos de torneos cancelados/eliminados.
        public async Task<int> BackfillAsync(CancellationToken cancellationToken)
        {
            var tournaments = await _tournamentRepository.GetAllAsync(cancellationToken);
            var existing = await _transactionRepository.GetAllAsync(cancellationToken);

            var withTransactions = existing
                .Select(t => t.TournamentId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);
            var validTournamentIds = tournaments.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

            var processed = 0;
            foreach (var tournament in tournaments)
            {
                var hasTransactions = withTransactions.Contains(tournament.Id);
                var shouldHaveTransactions =
                    tournament.Status == TournamentStatuses.InProgress ||
                    tournament.Status == TournamentStatuses.Finished;

                if ((shouldHaveTransactions && !hasTransactions) ||
                    (!shouldHaveTransactions && hasTransactions))
                {
                    await RebuildTournamentCoreAsync(tournament, cancellationToken);
                    processed++;
                }
            }

            var orphanTournamentIds = existing
                .Select(t => t.TournamentId)
                .Where(id => !string.IsNullOrWhiteSpace(id) && !validTournamentIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var orphanId in orphanTournamentIds)
            {
                await _transactionRepository.DeleteByTournamentAsync(orphanId, cancellationToken);
            }

            await RefreshAllCachesAsync(cancellationToken);
            return processed;
        }

        public async Task RemoveTournamentAsync(string tournamentId, CancellationToken cancellationToken)
        {
            await _transactionRepository.DeleteByTournamentAsync(tournamentId, cancellationToken);
            await RefreshAllCachesAsync(cancellationToken);
        }

        // RF-08: al desvincular un juego se borra su historial de ranking.
        public async Task RemoveUserGameAsync(string userId, string gameName, CancellationToken cancellationToken)
        {
            await _transactionRepository.DeleteByUserAndGameAsync(userId, gameName, cancellationToken);
            await RefreshAllCachesAsync(cancellationToken);
        }

        public async Task RemoveTeamAsync(string teamId, CancellationToken cancellationToken)
        {
            await _transactionRepository.DeleteTeamTransactionsAsync(teamId, cancellationToken);
            await RefreshAllCachesAsync(cancellationToken);
        }

        private async Task RebuildTournamentCoreAsync(Tournament tournament, CancellationToken cancellationToken)
        {
            var transactions = new List<PointTransaction>();

            if (tournament.Status == TournamentStatuses.InProgress ||
                tournament.Status == TournamentStatuses.Finished)
            {
                var matches = await _matchRepository.GetByTournamentAsync(tournament.Id, cancellationToken);
                var teams = await _teamRepository.GetAllAsync(cancellationToken);
                var users = await _userRepository.GetAllAsync(cancellationToken);
                transactions = BuildTransactions(tournament, matches, teams, users);
            }

            await _transactionRepository.ReplaceForTournamentAsync(tournament.Id, transactions, cancellationToken);
        }

        private static List<PointTransaction> BuildTransactions(
            Tournament tournament,
            List<TournamentMatch> matches,
            List<Team> teams,
            List<User> users)
        {
            var result = new List<PointTransaction>();
            var gameName = (tournament.GameName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(gameName)) return result;

            var teamsById = teams
                .Where(t => !string.IsNullOrWhiteSpace(t.Id))
                .GroupBy(t => t.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var usersById = users
                .Where(u => !string.IsNullOrWhiteSpace(u.Id))
                .GroupBy(u => u.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var disqualifiedUsers = tournament.Participants
                .Where(p => p.IsDisqualified)
                .Select(p => p.UserId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);
            var disqualifiedTeams = tournament.Participants
                .Where(p => p.IsDisqualified && !string.IsNullOrWhiteSpace(p.TeamId))
                .Select(p => p.TeamId!)
                .ToHashSet(StringComparer.Ordinal);

            // Plantilla congelada al inscribir: manda sobre el equipo actual. Si el
            // torneo es viejo y no tiene snapshot, se usa la plantilla del equipo.
            var rostersByTeamId = tournament.Participants
                .Where(p => !string.IsNullOrWhiteSpace(p.TeamId) && p.Roster is { Count: > 0 })
                .GroupBy(p => p.TeamId!, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.First().Roster
                        .Where(m => !string.IsNullOrWhiteSpace(m.UserId))
                        .Select(m => m.UserId)
                        .Distinct(StringComparer.Ordinal)
                        .ToList(),
                    StringComparer.Ordinal);

            var start = tournament.StartDate == default ? DateTime.UtcNow : tournament.StartDate.ToUniversalTime();
            var end = tournament.EndDate == default ? start.AddDays(1) : tournament.EndDate.ToUniversalTime();
            if (end < start) end = start.AddDays(1);

            bool HasGame(User user) =>
                (user.Games ?? new List<UserGame>())
                    .Any(g => IsSameGame(g.GameName, gameName));

            void AddUserCredit(
                string? userId,
                string? teamId,
                string type,
                int points,
                string description,
                DateTime occurredAt,
                string? matchId = null,
                int? round = null)
            {
                if (string.IsNullOrWhiteSpace(userId)) return;
                if (disqualifiedUsers.Contains(userId)) return;
                if (!string.IsNullOrWhiteSpace(teamId) && disqualifiedTeams.Contains(teamId)) return;
                if (!usersById.TryGetValue(userId, out var user) || !HasGame(user)) return;

                result.Add(new PointTransaction
                {
                    UserId = userId,
                    TeamId = teamId,
                    GameName = gameName,
                    TournamentId = tournament.Id,
                    TournamentTitle = tournament.Title,
                    Type = type,
                    Points = points,
                    Description = description,
                    MatchId = matchId,
                    Round = round,
                    CreatedAt = occurredAt
                });
            }

            List<string> MemberIds(string? teamId, string fallbackUserId)
            {
                if (!string.IsNullOrWhiteSpace(teamId))
                {
                    if (rostersByTeamId.TryGetValue(teamId, out var snapshot))
                    {
                        return snapshot;
                    }

                    if (teamsById.TryGetValue(teamId, out var team))
                    {
                        return team.Members
                            .Select(m => m.UserId)
                            .Where(id => !string.IsNullOrWhiteSpace(id))
                            .Distinct(StringComparer.Ordinal)
                            .ToList();
                    }
                }

                return string.IsNullOrWhiteSpace(fallbackUserId)
                    ? new List<string>()
                    : new List<string> { fallbackUserId };
            }

            void AddTeamCredit(
                string? teamId,
                string type,
                int points,
                string description,
                DateTime occurredAt,
                string? fallbackUserId = null,
                string? matchId = null,
                int? round = null)
            {
                if (string.IsNullOrWhiteSpace(teamId))
                {
                    AddUserCredit(fallbackUserId, null, type, points, description, occurredAt, matchId, round);
                    return;
                }

                if (disqualifiedTeams.Contains(teamId)) return;

                // El movimiento a nivel equipo solo se registra si el equipo todavía existe.
                if (teamsById.ContainsKey(teamId))
                {
                    result.Add(new PointTransaction
                    {
                        UserId = string.Empty,
                        TeamId = teamId,
                        GameName = gameName,
                        TournamentId = tournament.Id,
                        TournamentTitle = tournament.Title,
                        Type = type,
                        Points = points,
                        Description = description,
                        MatchId = matchId,
                        Round = round,
                        CreatedAt = occurredAt
                    });
                }

                // Los créditos individuales salen siempre de la plantilla del torneo.
                foreach (var memberId in MemberIds(teamId, fallbackUserId ?? string.Empty))
                {
                    AddUserCredit(memberId, teamId, type, points, description, occurredAt, matchId, round);
                }
            }

            void AddSlotCredit(
                MatchSlot slot,
                string type,
                int points,
                string description,
                DateTime occurredAt,
                string? matchId = null,
                int? round = null)
            {
                if (!string.IsNullOrWhiteSpace(slot.TeamId))
                {
                    AddTeamCredit(slot.TeamId, type, points, description, occurredAt, slot.UserId, matchId, round);
                }
                else
                {
                    AddUserCredit(slot.UserId, null, type, points, description, occurredAt, matchId, round);
                }
            }

            var isTeamTournament = string.Equals(tournament.TournamentType, "Equipo", StringComparison.OrdinalIgnoreCase);

            // Participación
            foreach (var participant in tournament.Participants.Where(p => !p.IsDisqualified))
            {
                if (isTeamTournament || !string.IsNullOrWhiteSpace(participant.TeamId))
                {
                    AddTeamCredit(
                        participant.TeamId,
                        PointTransactionTypes.Participation,
                        RankingPoints.Participation,
                        RankingPoints.Describe(PointTransactionTypes.Participation),
                        start,
                        participant.UserId);
                }
                else
                {
                    AddUserCredit(
                        participant.UserId,
                        null,
                        PointTransactionTypes.Participation,
                        RankingPoints.Participation,
                        RankingPoints.Describe(PointTransactionTypes.Participation),
                        start);
                }
            }

            // Partidos finalizados (los BYE no cuentan como partido jugado)
            foreach (var match in matches)
            {
                if (match.IsBye || match.Status != MatchStatuses.Finished || string.IsNullOrWhiteSpace(match.WinnerUserId))
                    continue;
                if (match.PlayerA is null || match.PlayerB is null) continue;

                MatchSlot? winnerSlot = null;
                MatchSlot? loserSlot = null;
                if (match.PlayerA.UserId == match.WinnerUserId)
                {
                    winnerSlot = match.PlayerA;
                    loserSlot = match.PlayerB;
                }
                else if (match.PlayerB.UserId == match.WinnerUserId)
                {
                    winnerSlot = match.PlayerB;
                    loserSlot = match.PlayerA;
                }

                if (winnerSlot is null || loserSlot is null) continue;

                var occurredAt = start.AddHours(Math.Max(1, match.Round));
                AddSlotCredit(
                    winnerSlot,
                    PointTransactionTypes.MatchWin,
                    RankingPoints.MatchWin,
                    RankingPoints.Describe(PointTransactionTypes.MatchWin, match.Round),
                    occurredAt,
                    match.Id,
                    match.Round);
                AddSlotCredit(
                    loserSlot,
                    PointTransactionTypes.MatchLoss,
                    RankingPoints.MatchLoss,
                    RankingPoints.Describe(PointTransactionTypes.MatchLoss, match.Round),
                    occurredAt,
                    match.Id,
                    match.Round);
            }

            // Bonus de posición: solo cuando el torneo está finalizado.
            if (tournament.Status != TournamentStatuses.Finished) return result;

            var finalRound = matches.Count == 0 ? 0 : matches.Max(m => m.Round);
            var final = matches.FirstOrDefault(m =>
                m.Round == finalRound &&
                m.Status == MatchStatuses.Finished &&
                !m.IsBye &&
                !string.IsNullOrWhiteSpace(m.WinnerUserId) &&
                m.PlayerA is not null &&
                m.PlayerB is not null);

            if (final is null) return result;

            MatchSlot? championSlot = null;
            MatchSlot? runnerUpSlot = null;
            if (final.PlayerA!.UserId == final.WinnerUserId)
            {
                championSlot = final.PlayerA;
                runnerUpSlot = final.PlayerB;
            }
            else if (final.PlayerB!.UserId == final.WinnerUserId)
            {
                championSlot = final.PlayerB;
                runnerUpSlot = final.PlayerA;
            }

            if (championSlot is null) return result;

            // El bonus de posición se registra después del último partido.
            var placementAt = end;
            var finalMatchAt = start.AddHours(Math.Max(1, finalRound + 1));
            if (placementAt < finalMatchAt) placementAt = finalMatchAt;

            AddSlotCredit(
                championSlot,
                PointTransactionTypes.Champion,
                RankingPoints.Champion,
                RankingPoints.Describe(PointTransactionTypes.Champion),
                placementAt,
                final.Id,
                final.Round);

            if (runnerUpSlot is not null)
            {
                AddSlotCredit(
                    runnerUpSlot,
                    PointTransactionTypes.RunnerUp,
                    RankingPoints.RunnerUp,
                    RankingPoints.Describe(PointTransactionTypes.RunnerUp),
                    placementAt,
                    final.Id,
                    final.Round);
            }

            if (tournament.Participants.Count >= RankingPoints.MinimumParticipantsForSemifinalist && finalRound >= 2)
            {
                var semifinalRound = finalRound - 1;
                foreach (var semifinal in matches.Where(m =>
                             m.Round == semifinalRound &&
                             m.Status == MatchStatuses.Finished &&
                             !m.IsBye &&
                             !string.IsNullOrWhiteSpace(m.WinnerUserId) &&
                             m.PlayerA is not null &&
                             m.PlayerB is not null))
                {
                    MatchSlot? semifinalLoser = null;
                    if (semifinal.PlayerA!.UserId == semifinal.WinnerUserId) semifinalLoser = semifinal.PlayerB;
                    else if (semifinal.PlayerB!.UserId == semifinal.WinnerUserId) semifinalLoser = semifinal.PlayerA;
                    if (semifinalLoser is null) continue;

                    AddSlotCredit(
                        semifinalLoser,
                        PointTransactionTypes.Semifinalist,
                        RankingPoints.Semifinalist,
                        RankingPoints.Describe(PointTransactionTypes.Semifinalist),
                        placementAt,
                        semifinal.Id,
                        semifinal.Round);
                }
            }

            return result;
        }

        // Recalcula los puntos cacheados en UserGame.Points / Team.Points y las
        // estadísticas globales del perfil a partir del ledger completo.
        private async Task RefreshAllCachesAsync(CancellationToken cancellationToken)
        {
            var transactions = await _transactionRepository.GetAllAsync(cancellationToken);
            var users = await _userRepository.GetAllAsync(cancellationToken);
            var teams = await _teamRepository.GetAllAsync(cancellationToken);

            var userAggregates = new Dictionary<(string UserId, string Game), Aggregate>(UserGameKeyComparer.Instance);
            foreach (var transaction in transactions.Where(t => !string.IsNullOrWhiteSpace(t.UserId)))
            {
                var key = (transaction.UserId, GameKey(transaction.GameName));
                if (!userAggregates.TryGetValue(key, out var aggregate))
                {
                    aggregate = new Aggregate();
                    userAggregates[key] = aggregate;
                }
                aggregate.Add(transaction);
            }

            var teamAggregates = new Dictionary<(string TeamId, string Game), Aggregate>(TeamGameKeyComparer.Instance);
            foreach (var transaction in transactions.Where(t =>
                         !string.IsNullOrWhiteSpace(t.TeamId) && string.IsNullOrWhiteSpace(t.UserId)))
            {
                var key = (transaction.TeamId!, GameKey(transaction.GameName));
                if (!teamAggregates.TryGetValue(key, out var aggregate))
                {
                    aggregate = new Aggregate();
                    teamAggregates[key] = aggregate;
                }
                aggregate.Add(transaction);
            }

            foreach (var user in users)
            {
                var games = user.Games ?? new List<UserGame>();
                var changed = false;

                var totalPoints = 0;
                var totalTournamentsWon = 0;
                var totalMatchesPlayed = 0;
                var totalMatchesWon = 0;
                var totalMatchesLost = 0;

                foreach (var game in games)
                {
                    var aggregate = userAggregates.GetValueOrDefault((user.Id, GameKey(game.GameName)));
                    var points = aggregate?.Points ?? 0;
                    var tournamentsPlayed = aggregate?.TournamentsPlayed ?? 0;
                    var tournamentsWon = aggregate?.TournamentsWon ?? 0;
                    var matchesPlayed = aggregate?.MatchesPlayed ?? 0;
                    var matchesWon = aggregate?.MatchesWon ?? 0;
                    var matchesLost = aggregate?.MatchesLost ?? 0;

                    if (game.Points != points ||
                        game.TournamentsPlayed != tournamentsPlayed ||
                        game.TournamentsWon != tournamentsWon ||
                        game.MatchesPlayed != matchesPlayed ||
                        game.MatchesWon != matchesWon ||
                        game.MatchesLost != matchesLost)
                    {
                        game.Points = points;
                        game.TournamentsPlayed = tournamentsPlayed;
                        game.TournamentsWon = tournamentsWon;
                        game.MatchesPlayed = matchesPlayed;
                        game.MatchesWon = matchesWon;
                        game.MatchesLost = matchesLost;
                        changed = true;
                    }

                    totalPoints += points;
                    totalTournamentsWon += tournamentsWon;
                    totalMatchesPlayed += matchesPlayed;
                    totalMatchesWon += matchesWon;
                    totalMatchesLost += matchesLost;
                }

                var stats = user.Stats ?? new UserStats();
                var winrate = totalMatchesPlayed > 0
                    ? Math.Round((double)totalMatchesWon / totalMatchesPlayed * 100, 1)
                    : 0;

                if (stats.Points != totalPoints ||
                    stats.TournamentsWon != totalTournamentsWon ||
                    stats.MatchesPlayed != totalMatchesPlayed ||
                    stats.MatchesWon != totalMatchesWon ||
                    stats.MatchesLost != totalMatchesLost ||
                    stats.Wins != totalMatchesWon ||
                    stats.Losses != totalMatchesLost ||
                    Math.Abs(stats.Winrate - winrate) > 0.001)
                {
                    stats.Points = totalPoints;
                    stats.TournamentsWon = totalTournamentsWon;
                    stats.MatchesPlayed = totalMatchesPlayed;
                    stats.MatchesWon = totalMatchesWon;
                    stats.MatchesLost = totalMatchesLost;
                    stats.Wins = totalMatchesWon;
                    stats.Losses = totalMatchesLost;
                    stats.Draws = 0;
                    stats.Winrate = winrate;
                    user.Stats = stats;
                    changed = true;
                }

                if (changed)
                {
                    await _userRepository.UpdateAsync(user, cancellationToken);
                }
            }

            foreach (var team in teams)
            {
                var aggregate = teamAggregates.GetValueOrDefault((team.Id, GameKey(team.GameName)));
                var points = aggregate?.Points ?? 0;
                var tournamentsPlayed = aggregate?.TournamentsPlayed ?? 0;
                var tournamentsWon = aggregate?.TournamentsWon ?? 0;
                var matchesPlayed = aggregate?.MatchesPlayed ?? 0;
                var matchesWon = aggregate?.MatchesWon ?? 0;
                var matchesLost = aggregate?.MatchesLost ?? 0;

                if (team.Points == points &&
                    team.TournamentsPlayed == tournamentsPlayed &&
                    team.TournamentsWon == tournamentsWon &&
                    team.MatchesPlayed == matchesPlayed &&
                    team.MatchesWon == matchesWon &&
                    team.MatchesLost == matchesLost)
                {
                    continue;
                }

                team.Points = points;
                team.TournamentsPlayed = tournamentsPlayed;
                team.TournamentsWon = tournamentsWon;
                team.MatchesPlayed = matchesPlayed;
                team.MatchesWon = matchesWon;
                team.MatchesLost = matchesLost;
                await _teamRepository.UpdateAsync(team, cancellationToken);
            }
        }

        private static int CalculatePosition(List<User> users, string userId, string gameName)
        {
            // Mismo criterio de desempate que GetLeaderboardAsync: puntos →
            // torneos ganados → partidos ganados → nombre → id.
            var rows = users
                .Where(u => u.IsActive)
                .Select(u =>
                {
                    var userGames = (u.Games ?? new List<UserGame>())
                        .Where(g => IsSameGame(g.GameName, gameName))
                        .ToList();

                    return new
                    {
                        UserId = u.Id,
                        Username = u.Username,
                        HasGame = userGames.Count > 0,
                        Points = userGames.Sum(g => g.Points),
                        TournamentsWon = userGames.Sum(g => g.TournamentsWon),
                        MatchesWon = userGames.Sum(g => g.MatchesWon)
                    };
                })
                .Where(u => u.HasGame)
                .OrderByDescending(u => u.Points)
                .ThenByDescending(u => u.TournamentsWon)
                .ThenByDescending(u => u.MatchesWon)
                .ThenBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.UserId, StringComparer.Ordinal)
                .ToList();

            var index = rows.FindIndex(u => u.UserId == userId);
            return index >= 0 ? index + 1 : 0;
        }

        private static PointTransactionDto MapTransaction(PointTransaction transaction) => new()
        {
            Id = transaction.Id,
            TournamentId = transaction.TournamentId,
            TournamentTitle = transaction.TournamentTitle,
            GameName = transaction.GameName,
            Type = transaction.Type,
            Points = transaction.Points,
            Description = transaction.Description,
            MatchId = transaction.MatchId,
            Round = transaction.Round,
            CreatedAt = transaction.CreatedAt
        };

        private static string? NormalizeGame(string? gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName)) return null;
            var game = gameName.Trim();
            if (game.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                game.Equals("global", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return game;
        }

        private static bool IsSameGame(string? left, string? right) =>
            string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

        private static string GameKey(string? gameName) => (gameName ?? string.Empty).Trim().ToLowerInvariant();

        private sealed class Aggregate
        {
            private readonly HashSet<string> _tournaments = new(StringComparer.Ordinal);

            public int Points { get; private set; }
            public int TournamentsWon { get; private set; }
            public int MatchesPlayed { get; private set; }
            public int MatchesWon { get; private set; }
            public int MatchesLost { get; private set; }
            public int TournamentsPlayed => _tournaments.Count;

            public void Add(PointTransaction transaction)
            {
                Points += transaction.Points;
                if (!string.IsNullOrWhiteSpace(transaction.TournamentId))
                {
                    _tournaments.Add(transaction.TournamentId);
                }

                switch (transaction.Type)
                {
                    case PointTransactionTypes.Champion:
                        TournamentsWon++;
                        break;
                    case PointTransactionTypes.MatchWin:
                        MatchesWon++;
                        MatchesPlayed++;
                        break;
                    case PointTransactionTypes.MatchLoss:
                        MatchesLost++;
                        MatchesPlayed++;
                        break;
                }
            }
        }

        private sealed class UserGameKeyComparer : IEqualityComparer<(string UserId, string Game)>
        {
            public static readonly UserGameKeyComparer Instance = new();

            public bool Equals((string UserId, string Game) x, (string UserId, string Game) y) =>
                string.Equals(x.UserId, y.UserId, StringComparison.Ordinal) &&
                string.Equals(x.Game, y.Game, StringComparison.Ordinal);

            public int GetHashCode((string UserId, string Game) obj) =>
                HashCode.Combine(
                    StringComparer.Ordinal.GetHashCode(obj.UserId),
                    StringComparer.Ordinal.GetHashCode(obj.Game));
        }

        private sealed class TeamGameKeyComparer : IEqualityComparer<(string TeamId, string Game)>
        {
            public static readonly TeamGameKeyComparer Instance = new();

            public bool Equals((string TeamId, string Game) x, (string TeamId, string Game) y) =>
                string.Equals(x.TeamId, y.TeamId, StringComparison.Ordinal) &&
                string.Equals(x.Game, y.Game, StringComparison.Ordinal);

            public int GetHashCode((string TeamId, string Game) obj) =>
                HashCode.Combine(
                    StringComparer.Ordinal.GetHashCode(obj.TeamId),
                    StringComparer.Ordinal.GetHashCode(obj.Game));
        }
    }
}
