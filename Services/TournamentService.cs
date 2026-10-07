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

            return tournaments.Select(t => new TournamentResponse(
                t.Id,
                t.Title,
                t.GameName,
                t.BannerUrl,
                t.PrizePool,
                t.Status,
                t.MaxParticipants,
                t.Rules,
                t.RankConditions,
                t.Participants.Select(p => new TournamentParticipantDto(
                    p.UserId,
                    p.Username,
                    p.InGameName,
                    p.Rank,
                    p.RegisteredAt
                )).ToList()
            )).ToList();
        }

        public async Task<TournamentResponse?> GetTournamentByIdAsync(string id, CancellationToken cancellationToken)
        {
            var t = await _tournamentRepository.GetByIdAsync(id, cancellationToken);
            if (t == null) return null;

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
                t.Participants.Select(p => new TournamentParticipantDto(
                    p.UserId,
                    p.Username,
                    p.InGameName,
                    p.Rank,
                    p.RegisteredAt
                )).ToList()
            );
        }

        public async Task CreateTournamentAsync(CreateTournamentRequest request, CancellationToken cancellationToken)
        {
            var tournament = new Tournament
            {
                Title = request.Title,
                GameName = request.GameName,
                BannerUrl = request.BannerUrl,
                PrizePool = request.PrizePool,
                MaxParticipants = request.MaxParticipants,
                Rules = request.Rules,
                RankConditions = request.RankConditions,
                Status = "Próximo",
                CreatedAt = DateTime.UtcNow
            };

            await _tournamentRepository.CreateAsync(tournament, cancellationToken);
        }

        public async Task<bool> RegisterParticipantAsync(string tournamentId, TournamentParticipantDto participantDto, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentRepository.GetByIdAsync(tournamentId, cancellationToken);
            if (tournament == null) return false;

            // Evitar doble inscripción
            if (tournament.Participants.Any(p => p.UserId == participantDto.UserId))
            {
                return false;
            }

            // Validar cupos
            if (tournament.Participants.Count >= tournament.MaxParticipants)
            {
                return false;
            }

            var participant = new TournamentParticipant
            {
                UserId = participantDto.UserId,
                Username = participantDto.Username,
                InGameName = participantDto.InGameName,
                Rank = participantDto.Rank,
                RegisteredAt = DateTime.UtcNow
            };

            // 🌟 CancellationToken agregado correctamente aquí
            return await _tournamentRepository.AddParticipantAsync(tournamentId, participant, cancellationToken);
        }
    }
}