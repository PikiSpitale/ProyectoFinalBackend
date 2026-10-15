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

        public TournamentService(ITournamentRepository tournamentRepository, IMatchRepository matchRepository)
        {
            _tournamentRepository = tournamentRepository;
            _matchRepository = matchRepository;
        }

        public async Task<List<TournamentResponse>> GetAllTournamentsAsync(CancellationToken cancellationToken)
        {
            var tournaments = await _tournamentRepository.GetAllAsync(cancellationToken);
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

            var tournament = new Tournament
            {
                Title = request.Title,
                GameName = request.GameName,
                BannerUrl = request.BannerUrl,
                PrizePool = request.PrizePool,
                MaxParticipants = request.MaxParticipants,
                Rules = request.Rules,
                RankConditions = request.RankConditions,
                StartDate = request.StartDate.ToUniversalTime(),
                EndDate = request.EndDate.ToUniversalTime(),
                Participants = new List<TournamentParticipant>()
            };

            await _tournamentRepository.CreateAsync(tournament, cancellationToken);
        }

        public async Task<bool> RegisterParticipantAsync(string tournamentId, TournamentParticipantDto participantDto, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentRepository.GetByIdAsync(tournamentId, cancellationToken);
            if (tournament is null) return false;

            // La inscripción solo está abierta mientras el torneo no haya comenzado
            if (tournament.Status != TournamentStatuses.Upcoming) return false;
            if (tournament.Participants.Count >= tournament.MaxParticipants) return false;
            if (tournament.Participants.Any(p => p.UserId == participantDto.UserId)) return false;

            var participant = new TournamentParticipant
            {
                UserId = participantDto.UserId,
                Username = participantDto.Username,
                InGameName = participantDto.InGameName,
                Rank = participantDto.Rank,
                RegisteredAt = DateTime.UtcNow
            };

            return await _tournamentRepository.AddParticipantAsync(tournamentId, participant, cancellationToken);
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

            // Si cambia el ganador pero el siguiente partido ya se jugó, hay que corregir ese primero
            var next = FindNextMatch(matches, match);
            if (next is not null && next.Status == MatchStatuses.Finished && match.WinnerUserId != winnerId)
            {
                throw new InvalidOperationException("El partido siguiente ya tiene resultado. Corregí ese resultado primero.");
            }

            match.ScoreA = request.ScoreA;
            match.ScoreB = request.ScoreB;

            await ApplyWinnerAsync(tournament, matches, match, winnerId, false, disqualified, cancellationToken);
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
                var disqualified = GetDisqualifiedIds(tournament);
                await ApplyWinnerAsync(tournament, matches, match, winnerId, false, disqualified, cancellationToken);
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

        // Define al ganador de un partido y lo avanza a la ronda siguiente.
        // Si era la final, el torneo termina.
        private async Task ApplyWinnerAsync(
            Tournament tournament,
            List<TournamentMatch> matches,
            TournamentMatch match,
            string winnerId,
            bool isWalkover,
            HashSet<string> disqualified,
            CancellationToken cancellationToken)
        {
            var previousWinnerId = match.WinnerUserId;

            match.WinnerUserId = winnerId;
            match.Status = MatchStatuses.Finished;
            match.IsWalkover = isWalkover;
            await _matchRepository.UpdateAsync(match, cancellationToken);

            var next = FindNextMatch(matches, match);

            if (next is null)
            {
                // Era la final: el torneo termina
                await _tournamentRepository.TryChangeStatusAsync(
                    tournament.Id, TournamentStatuses.InProgress, TournamentStatuses.Finished, cancellationToken);
                return;
            }

            // Se pone al ganador en el lugar que le corresponde del partido siguiente
            if (previousWinnerId != winnerId)
            {
                var winner = match.PlayerA!.UserId == winnerId ? match.PlayerA : match.PlayerB!;
                var slot = new MatchSlot
                {
                    UserId = winner.UserId,
                    Username = winner.Username,
                    InGameName = winner.InGameName
                };

                if (match.Position % 2 == 0) next.PlayerA = slot;
                else next.PlayerB = slot;

                await _matchRepository.UpdateAsync(next, cancellationToken);
            }

            // Si el rival que esperaba ahí está descalificado, el que llega gana sin jugar
            await ResolveDisqualifiedAsync(tournament, matches, next, disqualified, cancellationToken);
        }

        // Si en un partido exactamente uno de los dos está descalificado, gana el otro (walkover)
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
            await ApplyWinnerAsync(tournament, matches, match, winnerId, true, disqualified, cancellationToken);
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
                        p.DisqualificationReason))
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
            slot is null ? null : new MatchSlotDto(slot.UserId, slot.Username, slot.InGameName);
    }
}