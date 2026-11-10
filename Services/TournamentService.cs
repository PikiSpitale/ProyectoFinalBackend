using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Repositories;

namespace ProyectAPI.Services
{
    public sealed class TournamentService : ITournamentService
    {
        private const int MinParticipantsForBracket = 2;

        // La inscripción abre 30 minutos antes del inicio y se cierra puntualmente
        // en la fecha de inicio. Por eso un torneo nuevo debe crearse con al menos
        // 30 minutos de anticipación.
        private static readonly TimeSpan RegistrationWindow = TimeSpan.FromMinutes(30);
        private const int MaxStarters = 11;
        private const int MaxSubstitutes = 11;

        private readonly ITournamentRepository _tournamentRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly ITeamRepository _teamRepository;
        private readonly IUserRepository _userRepository;
        private readonly RankingService _rankingService;
        private readonly ILogger<TournamentService> _logger;

        public TournamentService(
            ITournamentRepository tournamentRepository,
            IMatchRepository matchRepository,
            ITeamRepository teamRepository,
            IUserRepository userRepository,
            RankingService rankingService,
            ILogger<TournamentService> logger)
        {
            _tournamentRepository = tournamentRepository;
            _matchRepository = matchRepository;
            _teamRepository = teamRepository;
            _userRepository = userRepository;
            _rankingService = rankingService;
            _logger = logger;
        }

        public async Task<List<TournamentResponse>> GetAllTournamentsAsync(CancellationToken cancellationToken) =>
            await GetAllTournamentsAsync(null, null, null, null, null, false, cancellationToken);

        public async Task<List<TournamentResponse>> GetAllTournamentsAsync(string? gameName, string? status, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken) =>
            await GetAllTournamentsAsync(gameName, status, fromDate, toDate, null, false, cancellationToken);

        // RF-19
        public async Task<List<TournamentResponse>> GetAllTournamentsAsync(string? gameName, string? status, DateTime? fromDate, DateTime? toDate, string? rank, bool includeCreator, CancellationToken cancellationToken)
        {
            var tournaments = await _tournamentRepository.GetAllAsync(gameName, status, fromDate, toDate, cancellationToken);
            if (!string.IsNullOrWhiteSpace(rank))
            {
                tournaments = tournaments.Where(t => MatchesRankFilter(t, rank.Trim())).ToList();
            }

            return tournaments.Select(t => MapToResponse(t, includeCreator)).ToList();
        }

        public async Task<TournamentResponse?> GetTournamentByIdAsync(string id, bool includeCreator, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentRepository.GetByIdAsync(id, cancellationToken);
            return tournament is null ? null : MapToResponse(tournament, includeCreator);
        }

        public async Task CreateTournamentAsync(CreateTournamentRequest request, string creatorId, CancellationToken cancellationToken)
        {
            var title = (request.Title ?? string.Empty).Trim();
            var gameName = (request.GameName ?? string.Empty).Trim();
            if (title.Length < 3)
                throw new InvalidOperationException("El título del torneo debe tener al menos 3 caracteres.");
            if (string.IsNullOrWhiteSpace(gameName))
                throw new InvalidOperationException("Debés indicar el videojuego del torneo.");
            if (request.MaxParticipants < 2)
                throw new InvalidOperationException("El cupo máximo debe ser de al menos 2 participantes.");
            if (request.PrizePool < 0)
                throw new InvalidOperationException("El premio no puede ser negativo.");
            if (request.StartDate == default || request.EndDate == default)
                throw new InvalidOperationException("Debés indicar la fecha y hora de inicio y de fin del torneo.");

            var startDate = request.StartDate.ToUniversalTime();
            var endDate = request.EndDate.ToUniversalTime();
            if (endDate < startDate)
                throw new InvalidOperationException("La fecha de finalización no puede ser anterior a la fecha de inicio del torneo.");

            EnsureStartDateIsInTheFuture(startDate);

            var type = string.IsNullOrWhiteSpace(request.TournamentType) ? "Individual" : request.TournamentType.Trim();
            if (type != "Individual" && type != "Equipo")
                throw new InvalidOperationException("El tipo de torneo debe ser 'Individual' o 'Equipo'.");

            var (starters, substitutes) = ResolveTeamFormat(gameName, type, request.StartersCount, request.SubstitutesCount);

            var rankConditions = (request.RankConditions ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rankConditions)) rankConditions = "Sin restricción";
            ValidateRankConditions(gameName, rankConditions);

            var creator = await _userRepository.GetByIdAsync(creatorId, cancellationToken);

            var tournament = new Tournament
            {
                Title = title,
                GameName = gameName,
                BannerUrl = request.BannerUrl ?? string.Empty,
                PrizePool = request.PrizePool,
                MaxParticipants = request.MaxParticipants,
                TournamentType = type,
                StartersCount = starters,
                SubstitutesCount = substitutes,
                Rules = request.Rules ?? string.Empty,
                RankConditions = rankConditions,
                CreatedByUserId = creatorId ?? string.Empty,
                CreatedByUsername = creator?.Username ?? string.Empty,
                StartDate = startDate,
                EndDate = endDate,
                Participants = new List<TournamentParticipant>()
            };

            await _tournamentRepository.CreateAsync(tournament, cancellationToken);
        }

        // RF-17
        public async Task<TournamentResponse?> UpdateTournamentAsync(string id, UpdateTournamentRequest request, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentRepository.GetByIdAsync(id, cancellationToken);
            if (tournament is null) return null;

            if (tournament.Status != TournamentStatuses.Upcoming)
            {
                throw new InvalidOperationException("Solo se pueden modificar torneos que aún no han comenzado.");
            }

            var newTitle = string.IsNullOrWhiteSpace(request.Title) ? tournament.Title : request.Title.Trim();
            var newGame = string.IsNullOrWhiteSpace(request.GameName) ? tournament.GameName : request.GameName.Trim();
            var newRankConditions = request.RankConditions is null
                ? tournament.RankConditions
                : request.RankConditions.Trim();

            if (newTitle.Length < 3)
                throw new InvalidOperationException("El título del torneo debe tener al menos 3 caracteres.");
            if (string.IsNullOrWhiteSpace(newGame))
                throw new InvalidOperationException("Debés indicar el videojuego del torneo.");
            if (string.IsNullOrWhiteSpace(newRankConditions)) newRankConditions = "Sin restricción";

            if (!string.Equals(newGame, tournament.GameName, StringComparison.OrdinalIgnoreCase) && tournament.Participants.Count > 0)
                throw new InvalidOperationException("No se puede cambiar el videojuego cuando ya hay inscriptos.");
            ValidateRankConditions(newGame, newRankConditions);

            tournament.Title = newTitle;
            tournament.GameName = newGame;
            if (request.BannerUrl is not null) tournament.BannerUrl = request.BannerUrl;
            if (request.PrizePool.HasValue)
            {
                if (request.PrizePool.Value < 0)
                    throw new InvalidOperationException("El premio no puede ser negativo.");
                tournament.PrizePool = request.PrizePool.Value;
            }
            if (request.Rules is not null) tournament.Rules = request.Rules;
            tournament.RankConditions = newRankConditions;

            if (request.MaxParticipants.HasValue)
            {
                if (request.MaxParticipants.Value < 2)
                    throw new InvalidOperationException("El cupo máximo debe ser de al menos 2 participantes.");
                if (request.MaxParticipants.Value < tournament.Participants.Count)
                    throw new InvalidOperationException("El cupo máximo no puede ser menor a la cantidad de inscritos.");
                tournament.MaxParticipants = request.MaxParticipants.Value;
            }

            if (!string.IsNullOrWhiteSpace(request.TournamentType))
            {
                if (tournament.Participants.Count > 0)
                    throw new InvalidOperationException("No se puede cambiar el tipo de torneo cuando ya hay inscritos.");
                var type = request.TournamentType.Trim();
                if (type != "Individual" && type != "Equipo")
                    throw new InvalidOperationException("El tipo de torneo debe ser 'Individual' o 'Equipo'.");
                tournament.TournamentType = type;
            }

            if (string.Equals(tournament.TournamentType, "Equipo", StringComparison.OrdinalIgnoreCase))
            {
                var currentStarters = tournament.StartersCount > 0
                    ? tournament.StartersCount
                    : GetDefaultTeamFormat(tournament.GameName).Starters;
                var currentSubstitutes = tournament.StartersCount > 0
                    ? tournament.SubstitutesCount
                    : GetDefaultTeamFormat(tournament.GameName).Substitutes;

                var (starters, substitutes) = ResolveTeamFormat(
                    tournament.GameName,
                    tournament.TournamentType,
                    request.StartersCount ?? currentStarters,
                    request.SubstitutesCount ?? currentSubstitutes);

                if (tournament.Participants.Count > 0 &&
                    (starters != currentStarters || substitutes != currentSubstitutes))
                {
                    throw new InvalidOperationException("No se puede cambiar el formato de equipo cuando ya hay inscritos.");
                }

                tournament.StartersCount = starters;
                tournament.SubstitutesCount = substitutes;
            }
            else if (tournament.StartersCount <= 0)
            {
                // Torneos viejos sin formato guardado: se completa con el default del juego.
                var (starters, substitutes) = ResolveTeamFormat(
                    tournament.GameName, tournament.TournamentType, null, null);
                tournament.StartersCount = starters;
                tournament.SubstitutesCount = substitutes;
            }

            var newStart = request.StartDate?.ToUniversalTime() ?? tournament.StartDate;
            var newEnd = request.EndDate?.ToUniversalTime() ?? tournament.EndDate;
            if (newEnd < newStart)
                throw new InvalidOperationException("La fecha de finalización no puede ser anterior a la fecha de inicio del torneo.");
            if (request.StartDate.HasValue &&
                Math.Abs((newStart - tournament.StartDate).TotalSeconds) >= 1)
            {
                EnsureStartDateIsInTheFuture(newStart);
            }
            tournament.StartDate = newStart;
            tournament.EndDate = newEnd;

            await _tournamentRepository.UpdateAsync(tournament, cancellationToken);
            return MapToResponse(tournament, includeCreator: true);
        }

        // RF-18
        public async Task<bool> CancelTournamentAsync(string id, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentRepository.GetByIdAsync(id, cancellationToken);
            if (tournament is null) return false;

            if (tournament.Status == TournamentStatuses.Finished)
                throw new InvalidOperationException("No se puede cancelar un torneo que ya finalizó.");
            if (tournament.Status == TournamentStatuses.Cancelled)
                return true;

            var cancelled = await _tournamentRepository.SetStatusAsync(id, TournamentStatuses.Cancelled, cancellationToken);
            if (cancelled)
            {
                // Un torneo cancelado no otorga puntos de ranking.
                await RebuildRankingAsync(id, cancellationToken);
            }

            return cancelled;
        }

        // RF-22
        public async Task<bool> VerifyParticipantRankAsync(string tournamentId, string userId, bool verified, string? verifiedBy, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentRepository.GetByIdAsync(tournamentId, cancellationToken);
            if (tournament is null) return false;
            if (!tournament.Participants.Any(p => p.UserId == userId)) return false;
            return await _tournamentRepository.SetParticipantRankVerificationAsync(tournamentId, userId, verified, verifiedBy, cancellationToken);
        }

        public async Task<bool> RegisterParticipantAsync(string tournamentId, TournamentParticipantDto participantDto, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentRepository.GetByIdAsync(tournamentId, cancellationToken);
            if (tournament is null) throw new InvalidOperationException("Torneo no encontrado.");

            if (tournament.Status != TournamentStatuses.Upcoming)
                throw new InvalidOperationException("La inscripción no está disponible: el torneo ya comenzó, finalizó o fue cancelado.");

            if (tournament.Participants.Count >= tournament.MaxParticipants)
                throw new InvalidOperationException("No hay cupos disponibles en este torneo.");

            var opensAt = tournament.StartDate.AddMinutes(-30);
            if (DateTime.UtcNow < opensAt)
                throw new InvalidOperationException($"La inscripción se habilita 30 minutos antes del inicio ({opensAt.ToLocalTime():g}).");

            EnsureRegistrationIsOpen(tournament);

            if (tournament.TournamentType == "Equipo")
                throw new InvalidOperationException("Para inscribirse a un torneo por equipos, debés usar el endpoint de inscripción por equipo.");

            if (tournament.Participants.Any(p => p.UserId == participantDto.UserId))
                throw new InvalidOperationException("Ya estás inscripto en este torneo.");

            var user = await _userRepository.GetByIdAsync(participantDto.UserId, cancellationToken);
            var userGame = user?.Games?.FirstOrDefault(g => g.GameName.Equals(tournament.GameName, StringComparison.OrdinalIgnoreCase));
            if (user is null || userGame is null || string.IsNullOrWhiteSpace(userGame.InGameName) || string.IsNullOrWhiteSpace(userGame.Rank))
                throw new InvalidOperationException("Debes tener el videojuego asociado a tu perfil con nombre de usuario y rango para inscribirte.");

            // RF-20: verificar límites de rango cuando la condición es interpretable automáticamente
            ValidateRankCondition(tournament, userGame.Rank, user.Username ?? "Jugador");

            var participant = new TournamentParticipant
            {
                UserId = participantDto.UserId,
                Username = string.IsNullOrWhiteSpace(participantDto.Username) ? (user.Username ?? "Jugador") : participantDto.Username,
                InGameName = userGame.InGameName,
                Rank = userGame.Rank,
                RegisteredAt = DateTime.UtcNow
            };

            var added = await _tournamentRepository.AddParticipantAsync(tournamentId, participant, cancellationToken);
            if (!added)
                throw new InvalidOperationException("No se pudo completar la inscripción. Verificá que sigas cumpliendo los requisitos e intentá de nuevo.");
            return true;
        }

        public async Task<bool> RegisterTeamAsync(string tournamentId, string teamId, string currentUserId, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentRepository.GetByIdAsync(tournamentId, cancellationToken);
            if (tournament is null) throw new InvalidOperationException("Torneo no encontrado.");

            if (tournament.Status != TournamentStatuses.Upcoming)
                throw new InvalidOperationException("La inscripción no está disponible: el torneo ya comenzó, finalizó o fue cancelado.");

            if (tournament.Participants.Count >= tournament.MaxParticipants)
                throw new InvalidOperationException("No hay cupos disponibles en este torneo.");

            var opensAt = tournament.StartDate.AddMinutes(-30);
            if (DateTime.UtcNow < opensAt)
                throw new InvalidOperationException($"La inscripción se habilita 30 minutos antes del inicio ({opensAt.ToLocalTime():g}).");

            EnsureRegistrationIsOpen(tournament);

            if (tournament.TournamentType != "Equipo")
                throw new InvalidOperationException("Este torneo es individual.");

            if (tournament.Participants.Any(p => p.TeamId == teamId))
                throw new InvalidOperationException("Este equipo ya está inscripto en el torneo.");

            var team = await _teamRepository.GetByIdAsync(teamId, cancellationToken);
            if (team == null) throw new InvalidOperationException("Equipo no encontrado.");
            if (team.CaptainId != currentUserId) throw new InvalidOperationException("Solo el capitán puede inscribir el equipo al torneo.");
            if (!string.IsNullOrWhiteSpace(team.GameName) && !team.GameName.Equals(tournament.GameName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("El equipo debe pertenecer al mismo videojuego que el torneo.");

            var starters = team.Members.Where(m => m.IsStarter).ToList();
            var substitutes = team.Members.Where(m => !m.IsStarter).ToList();
            var (requiredStarters, allowedSubstitutes) = ResolveTeamFormat(
                tournament.GameName,
                tournament.TournamentType,
                tournament.StartersCount > 0 ? tournament.StartersCount : null,
                tournament.StartersCount > 0 ? tournament.SubstitutesCount : null);

            if (starters.Count != requiredStarters)
                throw new InvalidOperationException(
                    $"El equipo debe presentar exactamente {requiredStarters} titular(es) para este torneo (tiene {starters.Count}).");
            if (substitutes.Count > allowedSubstitutes)
                throw new InvalidOperationException(
                    $"El equipo admite hasta {allowedSubstitutes} suplente(s) para este torneo (tiene {substitutes.Count}).");

            foreach (var member in team.Members)
            {
                var memberUser = await _userRepository.GetByIdAsync(member.UserId, cancellationToken);
                var mg = memberUser?.Games?.FirstOrDefault(g => g.GameName.Equals(tournament.GameName, StringComparison.OrdinalIgnoreCase));
                if (mg is null || string.IsNullOrWhiteSpace(mg.InGameName) || string.IsNullOrWhiteSpace(mg.Rank))
                    throw new InvalidOperationException($"El integrante {member.Username} debe tener el videojuego asociado con nombre de usuario y rango.");

                // RF-20/RF-21: verificar límites de rango de los integrantes cuando es interpretable
                ValidateRankCondition(tournament, mg.Rank, member.Username ?? "Integrante");
            }

            var captain = team.Members.FirstOrDefault(m => m.UserId == team.CaptainId);
            var captainUser = captain is null ? null : await _userRepository.GetByIdAsync(captain.UserId, cancellationToken);
            var captainGame = captainUser?.Games?.FirstOrDefault(g => g.GameName.Equals(tournament.GameName, StringComparison.OrdinalIgnoreCase));

            // Plantilla congelada: los puntos del torneo se acreditan a estos
            // integrantes aunque después el equipo cambie o se elimine.
            var roster = team.Members
                .Where(m => !string.IsNullOrWhiteSpace(m.UserId))
                .Select(m => new TournamentRosterMember { UserId = m.UserId, Username = m.Username })
                .ToList();

            var participant = new TournamentParticipant
            {
                UserId = team.CaptainId,
                Username = captain?.Username ?? team.Name,
                InGameName = captainGame?.InGameName ?? team.Name,
                Rank = captainGame?.Rank ?? string.Empty,
                RegisteredAt = DateTime.UtcNow,
                TeamId = team.Id,
                TeamName = team.Name,
                Roster = roster
            };

            var added = await _tournamentRepository.AddParticipantAsync(tournamentId, participant, cancellationToken);
            if (!added)
                throw new InvalidOperationException("No se pudo completar la inscripción. Verificá que sigas cumpliendo los requisitos e intentá de nuevo.");
            return true;
        }

        public async Task<bool> DeleteTournamentAsync(string id, CancellationToken cancellationToken)
        {
            var deleted = await _tournamentRepository.DeleteAsync(id, cancellationToken);

            // Si el torneo tenía llaves, se borran también sus enfrentamientos
            if (deleted)
            {
                await _matchRepository.DeleteByTournamentAsync(id, cancellationToken);
                await _rankingService.RemoveTournamentAsync(id, cancellationToken);
            }

            return deleted;
        }

        public async Task<List<MatchResponse>?> GetMatchesAsync(string tournamentId, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentRepository.GetByIdAsync(tournamentId, cancellationToken);
            if (tournament is null) return null;

            var matches = await _matchRepository.GetByTournamentAsync(tournamentId, cancellationToken);
            return MapMatches(matches);
        }

        public async Task<List<MatchResponse>?> GenerateBracketAsync(string tournamentId, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentRepository.GetByIdAsync(tournamentId, cancellationToken);
            if (tournament is null) return null;

            var matches = await BuildAndSaveBracketAsync(tournament, cancellationToken);
            return MapMatches(matches);
        }

        public async Task<int> GenerateDueBracketsAsync(CancellationToken cancellationToken)
        {
            var dueTournaments = await _tournamentRepository.GetDueForBracketAsync(DateTime.UtcNow, cancellationToken);
            var generated = 0;

            foreach (var tournament in dueTournaments)
            {
                // Sin rivales suficientes todavía: se vuelve a intentar en la próxima pasada
                if (tournament.Participants.Count(p => !p.IsDisqualified) < MinParticipantsForBracket) continue;

                try
                {
                    await BuildAndSaveBracketAsync(tournament, cancellationToken);
                    generated++;
                }
                catch (InvalidOperationException)
                {
                    // Otro proceso (o un moderador) ya generó las llaves de este torneo
                }
            }

            return generated;
        }

        public async Task<List<MatchResponse>?> SetMatchResultAsync(string tournamentId, string matchId, MatchResultRequest request, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentRepository.GetByIdAsync(tournamentId, cancellationToken);
            if (tournament is null) return null;

            var matches = await _matchRepository.GetByTournamentAsync(tournamentId, cancellationToken);
            var match = matches.FirstOrDefault(m => m.Id == matchId);
            if (match is null) return null;

            EnsureMatchCanBeEdited(tournament, match);

            if (request.ScoreA < 0 || request.ScoreB < 0)
            {
                throw new InvalidOperationException("El puntaje no puede ser negativo.");
            }

            var disqualified = GetDisqualifiedIds(tournament);
            var winnerId = ResolveWinner(match, request, disqualified);
            var previousWinnerId = match.WinnerUserId;

            // Si cambia el ganador pero el siguiente partido ya se jugó, hay que corregir ese primero
            var next = FindNextMatch(matches, match);
            if (next is not null && next.Status == MatchStatuses.Finished && previousWinnerId != winnerId)
            {
                throw new InvalidOperationException("El partido siguiente ya tiene resultado. Corregí ese resultado primero.");
            }

            match.ScoreA = request.ScoreA;
            match.ScoreB = request.ScoreB;

            // RF-25 / RF-26 / RF-27: registrar o corregir el resultado define el ganador,
            // pero el avance a la ronda siguiente es una acción manual del moderador (RF-28).
            await SetWinnerAsync(match, winnerId, false, cancellationToken);

            // Si el ganador anterior ya había sido avanzado, se corrige también su lugar.
            if (next is not null && previousWinnerId is not null && previousWinnerId != winnerId)
            {
                var newWinner = GetSlotForWinner(match, winnerId);
                if (newWinner is not null && TryReplaceAdvancedSlot(next, match.Position, previousWinnerId, newWinner))
                {
                    await _matchRepository.UpdateAsync(next, cancellationToken);
                }
            }

            await RebuildRankingAsync(tournamentId, cancellationToken);
            return MapMatches(matches);
        }

        // RF-28: pasa manualmente al ganador de un partido ya finalizado a la ronda siguiente.
        public async Task<List<MatchResponse>?> AdvanceWinnerAsync(string tournamentId, string matchId, CancellationToken cancellationToken)
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

            if (match.IsBye)
            {
                throw new InvalidOperationException("Este jugador pasó directo de ronda: no hay ganador para avanzar.");
            }

            if (match.Status != MatchStatuses.Finished || string.IsNullOrWhiteSpace(match.WinnerUserId))
            {
                throw new InvalidOperationException("Primero registrá el resultado del partido para definir un ganador.");
            }

            await AdvanceWinnerToNextAsync(tournament, matches, match, GetDisqualifiedIds(tournament), cancellationToken);
            await RebuildRankingAsync(tournamentId, cancellationToken);
            return MapMatches(matches);
        }

        public async Task<List<MatchResponse>?> AdjustMatchScoreAsync(string tournamentId, string matchId, MatchScoreRequest request, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentRepository.GetByIdAsync(tournamentId, cancellationToken);
            if (tournament is null) return null;

            var matches = await _matchRepository.GetByTournamentAsync(tournamentId, cancellationToken);
            var match = matches.FirstOrDefault(m => m.Id == matchId);
            if (match is null) return null;

            EnsureMatchCanBeEdited(tournament, match);

            if (match.Status == MatchStatuses.Finished)
            {
                throw new InvalidOperationException("El partido ya terminó. Usá «Editar resultado» para corregirlo.");
            }

            if (request.Delta != 1 && request.Delta != -1)
            {
                throw new InvalidOperationException("El cambio de puntaje debe ser +1 o -1.");
            }

            var side = request.Side?.Trim().ToUpperInvariant();
            if (side != "A" && side != "B")
            {
                throw new InvalidOperationException("El lado del partido debe ser A o B.");
            }

            var scoreA = match.ScoreA ?? 0;
            var scoreB = match.ScoreB ?? 0;

            if (side == "A") scoreA = Math.Max(0, scoreA + request.Delta);
            else scoreB = Math.Max(0, scoreB + request.Delta);

            match.ScoreA = scoreA;
            match.ScoreB = scoreB;

            // Victoria automática si el juego define un puntaje de victoria (ej. 13 en Valorant)
            var winScore = GameRules.GetWinScore(tournament.GameName);
            string? winnerId = null;
            if (winScore is not null)
            {
                if (scoreA >= winScore) winnerId = match.PlayerA!.UserId;
                else if (scoreB >= winScore) winnerId = match.PlayerB!.UserId;
            }

            if (winnerId is null)
            {
                await _matchRepository.UpdateAsync(match, cancellationToken);
            }
            else
            {
                // Al llegar al puntaje de victoria se define el ganador, pero el avance sigue siendo manual.
                await SetWinnerAsync(match, winnerId, false, cancellationToken);
            }

            await RebuildRankingAsync(tournamentId, cancellationToken);
            return MapMatches(matches);
        }

        public async Task<List<MatchResponse>?> DisqualifyParticipantAsync(string tournamentId, string userId, string? reason, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentRepository.GetByIdAsync(tournamentId, cancellationToken);
            if (tournament is null) return null;

            var participant = tournament.Participants.FirstOrDefault(p => p.UserId == userId);
            if (participant is null) return null;

            if (tournament.Status == TournamentStatuses.Finished)
            {
                throw new InvalidOperationException("El torneo ya finalizó, no se puede descalificar a nadie.");
            }

            if (participant.IsDisqualified)
            {
                throw new InvalidOperationException("Este jugador ya está descalificado.");
            }

            var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            var marked = await _tournamentRepository.DisqualifyParticipantAsync(tournamentId, userId, cleanReason, cancellationToken);
            if (!marked)
            {
                throw new InvalidOperationException("No se pudo descalificar al jugador.");
            }

            participant.IsDisqualified = true;
            participant.DisqualificationReason = cleanReason;

            var matches = await _matchRepository.GetByTournamentAsync(tournamentId, cancellationToken);

            // Si ya hay llaves y el jugador tenía un partido pendiente con rival, el rival gana
            var pending = matches.FirstOrDefault(m =>
                m.Status != MatchStatuses.Finished
                && !m.IsBye
                && (m.PlayerA?.UserId == userId || m.PlayerB?.UserId == userId));

            if (pending is not null)
            {
                var disqualified = GetDisqualifiedIds(tournament);
                await ResolveDisqualifiedAsync(tournament, matches, pending, disqualified, cancellationToken);
            }

            await RebuildRankingAsync(tournamentId, cancellationToken);
            return MapMatches(matches);
        }

        // ----- Lógica común de llaves -----

        // Define al ganador y marca el partido como finalizado, sin avanzar de ronda.
        private async Task SetWinnerAsync(
            TournamentMatch match,
            string winnerId,
            bool isWalkover,
            CancellationToken cancellationToken)
        {
            match.WinnerUserId = winnerId;
            match.Status = MatchStatuses.Finished;
            match.IsWalkover = isWalkover;
            await _matchRepository.UpdateAsync(match, cancellationToken);
        }

        // Pasa al ganador a la ronda siguiente. Si era la final, el torneo termina.
        private async Task AdvanceWinnerToNextAsync(
            Tournament tournament,
            List<TournamentMatch> matches,
            TournamentMatch match,
            HashSet<string> disqualified,
            CancellationToken cancellationToken)
        {
            var winnerId = match.WinnerUserId!;
            var next = FindNextMatch(matches, match);

            if (next is null)
            {
                // Era la final: el torneo termina
                await _tournamentRepository.TryChangeStatusAsync(
                    tournament.Id, TournamentStatuses.InProgress, TournamentStatuses.Finished, cancellationToken);
                return;
            }

            if (next.Status == MatchStatuses.Finished)
            {
                throw new InvalidOperationException("El partido siguiente ya tiene resultado.");
            }

            var winner = GetSlotForWinner(match, winnerId);
            if (winner is null)
            {
                throw new InvalidOperationException("No se pudo identificar al ganador del partido.");
            }

            // Se pone al ganador en el lugar que le corresponde del partido siguiente
            if (SetNextSlot(next, match.Position, winner))
            {
                await _matchRepository.UpdateAsync(next, cancellationToken);
            }

            // Si el rival que esperaba ahí está descalificado, el que llega gana sin jugar
            await ResolveDisqualifiedAsync(tournament, matches, next, disqualified, cancellationToken);
        }

        // Si en un partido exactamente uno de los dos está descalificado, gana el otro (walkover).
        // El walkover sí avanza automáticamente al ganador.
        private async Task ResolveDisqualifiedAsync(
            Tournament tournament,
            List<TournamentMatch> matches,
            TournamentMatch match,
            HashSet<string> disqualified,
            CancellationToken cancellationToken)
        {
            if (match.Status == MatchStatuses.Finished || match.IsBye) return;
            if (match.PlayerA is null || match.PlayerB is null) return;

            var aOut = disqualified.Contains(match.PlayerA.UserId);
            var bOut = disqualified.Contains(match.PlayerB.UserId);

            // Ninguno o los dos descalificados: lo resuelve el moderador a mano
            if (aOut == bOut) return;

            var winnerId = aOut ? match.PlayerB.UserId : match.PlayerA.UserId;
            await SetWinnerAsync(match, winnerId, true, cancellationToken);
            await AdvanceWinnerToNextAsync(tournament, matches, match, disqualified, cancellationToken);
        }

        private static MatchSlot? GetSlotForWinner(TournamentMatch match, string winnerId)
        {
            if (match.PlayerA?.UserId == winnerId) return match.PlayerA;
            if (match.PlayerB?.UserId == winnerId) return match.PlayerB;
            return null;
        }

        private static MatchSlot CloneSlot(MatchSlot slot) => new()
        {
            UserId = slot.UserId,
            Username = slot.Username,
            InGameName = slot.InGameName,
            TeamId = slot.TeamId,
            TeamName = slot.TeamName
        };

        // Coloca al ganador en el partido siguiente. Devuelve false si ya estaba ahí.
        private static bool SetNextSlot(TournamentMatch next, int position, MatchSlot winner)
        {
            var current = position % 2 == 0 ? next.PlayerA : next.PlayerB;
            if (current is not null && current.UserId == winner.UserId) return false;

            var slot = CloneSlot(winner);
            if (position % 2 == 0) next.PlayerA = slot;
            else next.PlayerB = slot;
            return true;
        }

        // Al corregir un resultado, reemplaza el lugar del ganador anterior solo si ya había avanzado.
        private static bool TryReplaceAdvancedSlot(TournamentMatch next, int position, string previousWinnerId, MatchSlot newWinner)
        {
            var current = position % 2 == 0 ? next.PlayerA : next.PlayerB;
            if (current is null || current.UserId != previousWinnerId) return false;

            var slot = CloneSlot(newWinner);
            if (position % 2 == 0) next.PlayerA = slot;
            else next.PlayerB = slot;
            return true;
        }

        private static void EnsureMatchCanBeEdited(Tournament tournament, TournamentMatch match)
        {
            if (tournament.Status != TournamentStatuses.InProgress && tournament.Status != TournamentStatuses.Finished)
            {
                throw new InvalidOperationException("El torneo no está en juego.");
            }

            if (match.IsBye)
            {
                throw new InvalidOperationException("Este jugador pasó directo de ronda: no hay resultado para cargar.");
            }

            if (match.PlayerA is null || match.PlayerB is null)
            {
                throw new InvalidOperationException("Todavía no están definidos los dos rivales de este partido.");
            }
        }

        // Si el moderador eligió ganador, se respeta (RF-30). Si no, gana el de mayor puntaje.
        private static string ResolveWinner(TournamentMatch match, MatchResultRequest request, HashSet<string> disqualified)
        {
            string winnerId;

            if (!string.IsNullOrWhiteSpace(request.WinnerUserId))
            {
                if (request.WinnerUserId != match.PlayerA!.UserId && request.WinnerUserId != match.PlayerB!.UserId)
                {
                    throw new InvalidOperationException("El ganador debe ser uno de los dos jugadores del partido.");
                }

                winnerId = request.WinnerUserId;
            }
            else
            {
                if (request.ScoreA == request.ScoreB)
                {
                    throw new InvalidOperationException("Hay empate: elegí manualmente quién gana.");
                }

                winnerId = request.ScoreA > request.ScoreB ? match.PlayerA!.UserId : match.PlayerB!.UserId;
            }

            if (disqualified.Contains(winnerId))
            {
                throw new InvalidOperationException("No se puede declarar ganador a un jugador descalificado.");
            }

            return winnerId;
        }

        private static TournamentMatch? FindNextMatch(List<TournamentMatch> matches, TournamentMatch match) =>
            matches.FirstOrDefault(m => m.Round == match.Round + 1 && m.Position == match.Position / 2);

        private static HashSet<string> GetDisqualifiedIds(Tournament tournament) =>
            tournament.Participants.Where(p => p.IsDisqualified).Select(p => p.UserId).ToHashSet();

        // Un torneo no puede crearse (ni reprogramarse) con menos de 30 minutos de
        // anticipación: ese es el tiempo mínimo para que los equipos se inscriban.
        private static void EnsureStartDateIsInTheFuture(DateTime startDateUtc)
        {
            var minimum = DateTime.UtcNow.Add(RegistrationWindow);
            // Pequeña tolerancia para diferencias de reloj/red sin perder la ventana.
            if (startDateUtc < minimum.AddSeconds(-30))
            {
                throw new InvalidOperationException(
                    $"La fecha de inicio debe ser al menos 30 minutos posterior a la hora actual para permitir la inscripción (mínimo: {minimum.ToLocalTime():g}).");
            }
        }

        // La inscripción se cierra exactamente en la fecha de inicio, sin esperar
        // a que el scheduler genere las llaves.
        private static void EnsureRegistrationIsOpen(Tournament tournament)
        {
            if (DateTime.UtcNow >= tournament.StartDate.ToUniversalTime())
            {
                throw new InvalidOperationException("La inscripción se cerró: el torneo ya llegó a su fecha de inicio.");
            }
        }

        private static (int Starters, int Substitutes) ResolveTeamFormat(
            string gameName,
            string tournamentType,
            int? starters,
            int? substitutes)
        {
            if (starters.HasValue && starters.Value < 1)
                throw new InvalidOperationException("La cantidad de titulares debe ser al menos 1.");
            if (substitutes.HasValue && substitutes.Value < 0)
                throw new InvalidOperationException("La cantidad de suplentes no puede ser negativa.");

            var (defaultStarters, defaultSubstitutes) = GetDefaultTeamFormat(gameName);
            var valueStarters = starters.HasValue && starters.Value > 0 ? starters.Value : defaultStarters;
            var valueSubstitutes = substitutes ?? defaultSubstitutes;

            if (valueStarters > MaxStarters)
                throw new InvalidOperationException($"La cantidad de titulares no puede superar {MaxStarters}.");
            if (valueSubstitutes > MaxSubstitutes)
                throw new InvalidOperationException($"La cantidad de suplentes no puede superar {MaxSubstitutes}.");

            // Los formatos por juego tienen límites propios (solo aplican a torneos de equipo).
            if (string.Equals(tournamentType, "Equipo", StringComparison.OrdinalIgnoreCase))
            {
                var game = NormalizeGameName(gameName);
                if (game.Contains("rocket", StringComparison.Ordinal) && valueStarters > 3)
                    throw new InvalidOperationException("Rocket League admite como máximo 3 titulares (modos 1v1, 2v2 y 3v3).");
                if (IsFivePlayerGame(game) && valueStarters != 5)
                    throw new InvalidOperationException($"{gameName} requiere exactamente 5 titulares.");
            }

            return (valueStarters, valueSubstitutes);
        }

        // Formato por defecto según el juego (los torneos viejos no lo tienen guardado).
        private static (int Starters, int Substitutes) GetDefaultTeamFormat(string gameName)
        {
            var game = NormalizeGameName(gameName);
            if (game.Contains("rocket", StringComparison.Ordinal)) return (3, 0);
            if (game.Contains("fifa", StringComparison.Ordinal) ||
                game.Contains("ea fc", StringComparison.Ordinal) ||
                game.Contains("fc 2", StringComparison.Ordinal)) return (11, 7);
            return (5, 2);
        }

        private static bool IsFivePlayerGame(string game) =>
            !game.Contains("rocket", StringComparison.Ordinal) &&
            (game.Contains("valorant", StringComparison.Ordinal) ||
             game.Contains("counter", StringComparison.Ordinal) ||
             game.Contains("cs2", StringComparison.Ordinal) ||
             game.Contains("cs:go", StringComparison.Ordinal) ||
             game.Contains("csgo", StringComparison.Ordinal) ||
             game.Contains("league", StringComparison.Ordinal) ||
             game.Contains("lol", StringComparison.Ordinal));

        private static string NormalizeGameName(string? gameName) => (gameName ?? string.Empty).Trim().ToLowerInvariant();

        // El ranking no debe romper la operación del torneo: si falla, se registra
        // el error y se deja el torneo consistente para el próximo recálculo.
        private async Task RebuildRankingAsync(string tournamentId, CancellationToken cancellationToken)
        {
            try
            {
                await _rankingService.RebuildTournamentAsync(tournamentId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo actualizar el ranking del torneo {TournamentId}.", tournamentId);
            }
        }

        // Valida, reserva el torneo de forma atómica y guarda las llaves
        private async Task<List<TournamentMatch>> BuildAndSaveBracketAsync(Tournament tournament, CancellationToken cancellationToken)
        {
            if (tournament.Status != TournamentStatuses.Upcoming)
            {
                throw new InvalidOperationException("El torneo ya comenzó, no se pueden generar las llaves.");
            }

            if (tournament.Participants.Count(p => !p.IsDisqualified) < MinParticipantsForBracket)
            {
                throw new InvalidOperationException("Se necesitan al menos 2 inscritos para generar las llaves.");
            }

            // El inicio manual de un moderador puede adelantarse a la fecha
            // programada; el cierre de la inscripción se valida al inscribir.

            // Pasa de "Próximo" a "En curso" solo una vez: si dos procesos intentan a la vez, uno falla
            var reserved = await _tournamentRepository.TryChangeStatusAsync(
                tournament.Id, TournamentStatuses.Upcoming, TournamentStatuses.InProgress, cancellationToken);

            if (!reserved)
            {
                throw new InvalidOperationException("Las llaves de este torneo ya fueron generadas.");
            }

            try
            {
                var matches = BracketGenerator.Generate(tournament);
                await _matchRepository.InsertManyAsync(matches, cancellationToken);
                // El torneo pasó a "En curso": ya corresponde acreditar la participación.
                await RebuildRankingAsync(tournament.Id, cancellationToken);
                return matches;
            }
            catch
            {
                // Si algo falla, el torneo vuelve a quedar abierto
                await _matchRepository.DeleteByTournamentAsync(tournament.Id, CancellationToken.None);
                await _tournamentRepository.TryChangeStatusAsync(
                    tournament.Id, TournamentStatuses.InProgress, TournamentStatuses.Upcoming, CancellationToken.None);
                throw;
            }
        }

        // RF-20: verificación automática de límites de rango cuando la condición es interpretable.
        // Casos ambiguos/no reconocibles se dejan para verificación manual (RF-22).
        private static void ValidateRankCondition(Tournament tournament, string userRank, string who)
        {
            var cond = (tournament.RankConditions ?? string.Empty).Trim();
            if (GameRankLadders.IsUnrestricted(cond)) return;
            if (!GameRankLadders.HasLadder(tournament.GameName)) return;
            if (string.IsNullOrWhiteSpace(userRank)) return;

            var mode = GameRankLadders.ParseCondition(tournament.GameName, cond, out var requiredRank);
            if (mode == GameRankLadders.RankConditionMode.None || string.IsNullOrWhiteSpace(requiredRank)) return;

            if (mode == GameRankLadders.RankConditionMode.Fixed)
            {
                if (!GameRankLadders.MatchesFixed(tournament.GameName, requiredRank, userRank))
                    throw new InvalidOperationException($"No cumple la condición de rango del torneo ({cond}). Se requiere exactamente {requiredRank} para {who} (rango actual: {userRank}).");
                return;
            }

            if (!GameRankLadders.MeetsMinimum(tournament.GameName, requiredRank, userRank))
                throw new InvalidOperationException($"No cumple la condición de rango del torneo ({cond}). Se requiere al menos {requiredRank} para {who} (rango actual: {userRank}).");
        }

        // Valida que una condición estructurada ("Mínimo: X" / "Fijo: X") use un rango reconocido.
        private static void ValidateRankConditions(string gameName, string? rankConditions)
        {
            if (!GameRankLadders.IsStructuredCondition(rankConditions)) return;

            var mode = GameRankLadders.ParseCondition(gameName, rankConditions, out var requiredRank);
            if (!GameRankLadders.HasLadder(gameName))
                throw new InvalidOperationException($"El juego '{gameName}' no tiene una escalera de rangos configurada. Usá 'Sin restricción' y verificá el rango manualmente (RF-22).");
            if (mode == GameRankLadders.RankConditionMode.None || string.IsNullOrWhiteSpace(requiredRank))
                throw new InvalidOperationException("La condición de rango no es válida. Elegí un rango reconocido para el videojuego del torneo.");
        }

        // RF-19: un torneo es compatible con el rango consultado si no tiene restricción
        // o si el rango cumple el mínimo/fijo. Condiciones libres se incluyen (no se pueden evaluar).
        private static bool MatchesRankFilter(Tournament tournament, string rank)
        {
            var cond = (tournament.RankConditions ?? string.Empty).Trim();
            if (GameRankLadders.IsUnrestricted(cond)) return true;

            var mode = GameRankLadders.ParseCondition(tournament.GameName, cond, out var requiredRank);
            if (mode == GameRankLadders.RankConditionMode.None || string.IsNullOrWhiteSpace(requiredRank)) return true;
            if (!GameRankLadders.IsRecognizedRank(tournament.GameName, rank)) return true;

            return mode == GameRankLadders.RankConditionMode.Fixed
                ? GameRankLadders.MatchesFixed(tournament.GameName, requiredRank, rank)
                : GameRankLadders.MeetsMinimum(tournament.GameName, requiredRank, rank);
        }

        // ----- Mapeos -----

        private static TournamentResponse MapToResponse(Tournament t, bool includeCreator = false)
        {
            var (defaultStarters, defaultSubstitutes) = GetDefaultTeamFormat(t.GameName);
            var starters = t.StartersCount > 0 ? t.StartersCount : defaultStarters;
            var substitutes = t.StartersCount > 0 ? t.SubstitutesCount : defaultSubstitutes;

            return new TournamentResponse(
                t.Id,
                t.Title,
                t.GameName,
                t.BannerUrl,
                t.PrizePool,
                t.Status,
                t.MaxParticipants,
                t.TournamentType,
                t.Rules,
                t.RankConditions,
                t.StartDate,
                t.EndDate,
                t.Participants
                    .Select(p => new TournamentParticipantDto(
                        p.UserId,
                        p.Username,
                        p.InGameName,
                        p.Rank,
                        p.RegisteredAt,
                        p.IsDisqualified,
                        p.DisqualificationReason,
                        p.RankVerified,
                        p.RankVerifiedBy,
                        p.RankVerifiedAt,
                        p.TeamId,
                        p.TeamName))
                    .ToList(),
                starters,
                substitutes,
                includeCreator && !string.IsNullOrWhiteSpace(t.CreatedByUsername) ? t.CreatedByUsername : null
            );
        }

        private static List<MatchResponse> MapMatches(IEnumerable<TournamentMatch> matches) =>
            matches
                .OrderBy(m => m.Round).ThenBy(m => m.Position)
                .Select(MapMatch)
                .ToList();

        private static MatchResponse MapMatch(TournamentMatch m)
        {
            return new MatchResponse(
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
        }

        private static MatchSlotDto? MapSlot(MatchSlot? slot) =>
            slot is null
                ? null
                : new MatchSlotDto(slot.UserId, slot.Username, slot.InGameName, slot.TeamId, slot.TeamName);
    }
}