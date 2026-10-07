using ProyectAPI.DTOs;

namespace ProyectAPI.Services
{
    public interface ITournamentService
    {
        Task<List<TournamentResponse>> GetAllTournamentsAsync(CancellationToken cancellationToken);
        Task<TournamentResponse?> GetTournamentByIdAsync(string id, CancellationToken cancellationToken);
        Task CreateTournamentAsync(CreateTournamentRequest request, CancellationToken cancellationToken);
        Task<bool> RegisterParticipantAsync(string tournamentId, TournamentParticipantDto participantDto, CancellationToken cancellationToken);
    }
}