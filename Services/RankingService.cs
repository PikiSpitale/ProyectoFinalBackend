using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Repositories;

namespace ProyectAPI.Services
{
    public sealed class RankingService
    {
        private readonly IUserRepository _userRepository;

        public RankingService(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<IEnumerable<UserRankingDto>> GetLeaderboardAsync(string? gameName, CancellationToken cancellationToken)
        {
            var users = await _userRepository.GetAllAsync(cancellationToken);

            var ranking = users
                .Select(u => new { User = u, Stats = u.Stats ?? new UserStats() })
                .OrderByDescending(x => x.Stats.Points)
                .ThenBy(x => x.User.Username)
                .Take(50)
                .Select(x => new UserRankingDto
                {
                    UserId = x.User.Id!,
                    Username = x.User.Username ?? "Usuario",
                    AvatarUrl = x.User.AvatarUrl,
                    Points = x.Stats.Points,
                    TournamentsWon = x.Stats.TournamentsWon,
                    MatchesPlayed = x.Stats.MatchesPlayed,
                    MatchesWon = x.Stats.MatchesWon,
                    MatchesLost = x.Stats.MatchesLost
                })
                .ToList();

            return ranking;
        }
    }
}