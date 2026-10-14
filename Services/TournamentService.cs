using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Repositories;

namespace ProyectAPI.Services
{
    public sealed class TournamentService : ITournamentService
    {
        private readonly ITournamentRepository _tournamentRepository;

        public TournamentService(ITournamentRepository tournamentRepository)
        {
            _tournamentRepository = tournamentRepository;
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
            return await _tournamentRepository.DeleteAsync(id, cancellationToken);
        }

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
                        p.RegisteredAt))
                    .ToList()
            );
        }
    }
}