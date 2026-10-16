using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Services;

namespace ProyectAPI.Controllers
{
    [ApiController]
    [Route("api/tournaments/{tournamentId}/matches")]
    public class MatchesController : ControllerBase
    {
        private readonly IMatchStatsService _matchStatsService;

        public MatchesController(IMatchStatsService matchStatsService)
        {
            _matchStatsService = matchStatsService;
        }

        // Detalle de un partido con sus estadísticas (lo puede ver cualquier usuario)
        [HttpGet("{matchId}")]
        public async Task<ActionResult<MatchDetailResponse>> GetDetail(string tournamentId, string matchId, CancellationToken cancellationToken)
        {
            var detail = await _matchStatsService.GetMatchDetailAsync(tournamentId, matchId, cancellationToken);
            if (detail == null) return NotFound(new { message = "Partido no encontrado." });
            return Ok(detail);
        }

        // Actualiza una estadística de un jugador (solo moderadores)
        [HttpPut("{matchId}/stats")]
        [Authorize(Roles = AppRoles.Staff)]
        public async Task<IActionResult> UpdateStat(string tournamentId, string matchId, [FromBody] MatchStatRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var detail = await _matchStatsService.UpdateStatAsync(tournamentId, matchId, request, cancellationToken);
                if (detail == null) return NotFound(new { message = "Partido no encontrado." });
                return Ok(detail);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}