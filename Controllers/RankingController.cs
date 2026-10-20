using Microsoft.AspNetCore.Mvc;
using ProyectAPI.DTOs;
using ProyectAPI.Services;

namespace ProyectAPI.Controllers
{
    [Route("api/rankings")]
    [ApiController]
    public class RankingsController : ControllerBase
    {
        private readonly RankingService _rankingService;

        public RankingsController(RankingService rankingService)
        {
            _rankingService = rankingService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserRankingDto>>> GetLeaderboard(
            [FromQuery] string? gameName,
            CancellationToken cancellationToken)
        {
            var leaderboard = await _rankingService.GetLeaderboardAsync(gameName, cancellationToken);
            return Ok(leaderboard);
        }
    }
}