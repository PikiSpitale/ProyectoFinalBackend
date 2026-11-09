using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Repositories;

namespace ProyectAPI.Services
{
    public sealed class TournamentService : ITournamentService
    {
        private const int MinParticipantsForBracket = 2;

        private readonly ITournamentRepository _tournamentRepository;
        private readonly IMatchRepository _matchRepository;
        private readonly ITeamRepository _teamRepository;
        private readonly IUserRepository _userRepository;

        public TournamentService(ITournamentRepository tournamentRepository, IMatchRepository matchRepository, ITeamRepository teamRepository, IUserRepository userRepository)
        {
            _tournamentRepository = tournamentRepository;
            _matchRepository = matchRepository;
            _teamRepository = teamRepository;
            _userRepository = userRepository;
        }

        public async Task<List<TournamentResponse>> GetAllTournamentsAsync(CancellationToken cancellationToken) =>
            await GetAllTournamentsAsync(null, null, null, null, cancellationToken);

        // RF-19
        public async Task<List<TournamentResponse>> GetAllTournamentsAsync(string? gameName, string? status, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken)
        {
            var tournaments = await _tournamentRepository.GetAllAsync(gameName, status, fromDate, toDate, cancellationToken);
            return tournaments.Select(MapToResponse).ToList();
        }

        public async Task<TournamentResponse?> GetTournamentByIdAsync(string id, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentRepository.GetByIdAsync(id, cancellationToken);
            return tournament is null ? null : MapToResponse(tournament);
        }

        public async Task CreateTournamentAsync(CreateTournamentRequest request, CancellationToken cancellationToken)
        {
            if (request.EndDate < request.StartDate)
            {
                throw new InvalidOperationException("La fecha de finalización no puede ser anterior a la fecha de inicio del torneo.");
            }

            var type = string.IsNullOrWhiteSpace(request.TournamentType) ? "Individual" : request.TournamentType.Trim();
            if (type != "Individual" && type != "Equipo")
            {
                throw new InvalidOperationException("El tipo de torneo debe ser 'Individual' o 'Equipo'.");
            }

            var tournament = new Tournament
            {
                Title = request.Title,
                GameName = request.GameName,
                BannerUrl = request.BannerUrl,
                PrizePool = request.PrizePool,
                MaxParticipants = request.MaxParticipants,
                TournamentType = type,
                Rules = request.Rules,
                RankConditions = request.RankConditions,
                StartDate = request.StartDate.ToUniversalTime(),
                EndDate = request.EndDate.ToUniversalTime(),
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

            if (!string.IsNullOrWhiteSpace(request.Title)) tournament.Title = request.Title.Trim();
            if (!string.IsNullOrWhiteSpace(request.GameName)) tournament.GameName = request.GameName.Trim();
            if (request.BannerUrl is not null) tournament.BannerUrl = request.BannerUrl;
            if (request.PrizePool.HasValue) tournament.PrizePool = request.PrizePool.Value;
            if (request.Rules is not null) tournament.Rules = request.Rules;
            if (request.RankConditions is not null) tournament.RankConditions = request.RankConditions;

            if (request.MaxParticipants.HasValue)
            {
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

            var newStart = request.StartDate?.ToUniversalTime() ?? tournament.StartDate;
            var newEnd = request.EndDate?.ToUniversalTime() ?? tournament.EndDate;
            if (newEnd < newStart)
                throw new InvalidOperationException("La fecha de finalización no puede ser anterior a la fecha de inicio del torneo.");
            tournament.StartDate = newStart;
            tournament.EndDate = newEnd;

            await _tournamentRepository.UpdateAsync(tournament, cancellationToken);
            return MapToResponse(tournament);
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

            return await _tournamentRepository.SetStatusAsync(id, TournamentStatuses.Cancelled, cancellationToken);
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
                Username = string.IsNullOrWhiteSpace(participantDto.Username) ? user.Username : participantDto.Username,
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
            if (starters.Count != 5 || substitutes.Count != 2)
                throw new InvalidOperationException("El equipo debe tener exactamente 5 titulares y 2 suplentes para inscribirse a torneos grupales.");

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

            var participant = new TournamentParticipant
            {
                UserId = team.CaptainId,
                Username = captain?.Username ?? team.Name,
                InGameName = captainGame?.InGameName ?? team.Name,
                Rank = captainGame?.Rank ?? string.Empty,
                RegisteredAt = DateTime.UtcNow,
                TeamId = team.Id,
                TeamName = team.Name
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

            // Ventana de inscripción: habilitar 30 minutos antes del inicio. Generación tras cierre (documentación).
            // Si aún no pasó la ventana, no forzamos, pero permitimos que moderador genere manualmente si corresponde.

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
            if (string.IsNullOrWhiteSpace(cond)) return;

            var lower = cond.ToLowerInvariant();
            if (lower is "-" or "n/a" || lower.Contains("libre") || lower.Contains("todos") || lower.Contains("sin restric") || lower.Contains("cualquiera") || lower.Contains("abierto") || lower.Contains("ningun"))
                return;

            if (!GameRankLadders.HasLadder(tournament.GameName)) return;
            if (string.IsNullOrWhiteSpace(userRank)) return;

            var minIntent = lower.Contains("mínimo") || lower.Contains("minimo") || lower.Contains("minimum") || lower.Contains("al menos") || lower.Contains("desde") || lower.Contains("superior") || lower.Contains(" o mas") || lower.Contains(" o más") || lower.Contains(">=") || cond.TrimEnd().EndsWith("+");
            var fixedIntent = lower.Contains("fijo") || lower.Contains("exacto") || lower.Contains("únicamente igual") || lower.Contains("unicamente igual");

            // Extraer rango candidato quitando prefijos comunes
            var candidate = cond;
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"(?i)^(condici[oó]n(es)?\s*(de\s*rango)?\s*[:\-]\s*)", "");
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"(?i)\b(rango\s*m[ií]nimo|m[ií]nimo|minimum|min|rango\s*fijo|fijo|exacto|solo|rango|al\s*menos|desde)\b\s*[:\-]?", "");
            candidate = candidate.Replace(">=", "").Replace(">", "").Trim().TrimEnd('+').Trim();
            // Quitar sufijos tipo "o superior"
            candidate = System.Text.RegularExpressions.Regex.Replace(candidate, @"(?i)\s*(o\s+superior|o\s+m[aá]s|en\s+adelante).*$", "").Trim();

            if (!GameRankLadders.IsRecognizedRank(tournament.GameName, candidate)) return;

            if (fixedIntent && !minIntent)
            {
                if (!GameRankLadders.MatchesFixed(tournament.GameName, candidate, userRank))
                    throw new InvalidOperationException($"No cumple la condición de rango del torneo ({cond}). Se requiere exactamente {candidate} para {who}.");
                return;
            }

            if (minIntent)
            {
                if (!GameRankLadders.MeetsMinimum(tournament.GameName, candidate, userRank))
                    throw new InvalidOperationException($"No cumple la condición de rango del torneo ({cond}). Se requiere al menos {candidate} para {who} (rango actual: {userRank}).");
            }
        }

        // ----- Mapeos -----

        private static TournamentResponse MapToResponse(Tournament t)
        {
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
                    .ToList()
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