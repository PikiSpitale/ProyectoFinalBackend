using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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

        // RF-31 / RF-34: ranking de jugadores, filtrable por juego.
        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserRankingDto>>> GetLeaderboard(
            [FromQuery] string? gameName,
            CancellationToken cancellationToken)
        {
            var leaderboard = await _rankingService.GetLeaderboardAsync(gameName, cancellationToken);
            return Ok(leaderboard);
        }

        // RF-32 / RF-34: ranking de equipos, filtrable por juego.
        [HttpGet("teams")]
        public async Task<ActionResult<IEnumerable<TeamRankingDto>>> GetTeamLeaderboard(
            [FromQuery] string? gameName,
            CancellationToken cancellationToken)
        {
            var leaderboard = await _rankingService.GetTeamLeaderboardAsync(gameName, cancellationToken);
            return Ok(leaderboard);
        }

        // RF-40: progreso e historial del jugador autenticado.
        [HttpGet("me")]
        [Authorize]
        public async Task<ActionResult<UserProgressDto>> GetMyProgress(CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

            var progress = await _rankingService.GetUserProgressAsync(userId, cancellationToken);
            if (progress is null) return NotFound(new { message = "Usuario no encontrado." });
            return Ok(progress);
        }

        // RF-40: progreso público de un jugador (para su perfil).
        [HttpGet("users/{userId}")]
        public async Task<ActionResult<UserProgressDto>> GetUserProgress(
            string userId,
            CancellationToken cancellationToken)
        {
            var progress = await _rankingService.GetUserProgressAsync(userId, cancellationToken);
            if (progress is null) return NotFound(new { message = "Usuario no encontrado." });
            return Ok(progress);
        }
    }
}
