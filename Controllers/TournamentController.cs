using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Services;
using System.Security.Claims;

namespace ProyectAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TournamentsController : ControllerBase
    {
        private readonly ITournamentService _tournamentService;

        public TournamentsController(ITournamentService tournamentService)
        {
            _tournamentService = tournamentService;
        }

        // RF-19: consulta y filtrado de torneos
        [HttpGet]
        public async Task<ActionResult<List<TournamentResponse>>> GetAll(
            [FromQuery] string? gameName,
            [FromQuery] string? status,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] string? rank,
            CancellationToken cancellationToken)
        {
            var tournaments = await _tournamentService.GetAllTournamentsAsync(gameName, status, fromDate, toDate, rank, cancellationToken);
            return Ok(tournaments);
        }

        // Endpoint para obtener un torneo por su ID
        [HttpGet("{id}")]
        public async Task<ActionResult<TournamentResponse>> GetById(string id, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentService.GetTournamentByIdAsync(id, cancellationToken);
            if (tournament == null) return NotFound(new { message = "Torneo no encontrado." });
            return Ok(tournament);
        }

        // Endpoint para crear un torneo con validación de fechas (solo moderadores)
        [HttpPost]
        [Authorize(Roles = AppRoles.Staff)]
        public async Task<IActionResult> Create([FromBody] CreateTournamentRequest request, CancellationToken cancellationToken)
        {
            try
            {
                await _tournamentService.CreateTournamentAsync(request, cancellationToken);
                return Ok(new { message = "Torneo creado con éxito." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // Endpoint para registrarse en un torneo individual
        [HttpPost("{id}/register")]
        [Authorize]
        public async Task<IActionResult> Register(string id, [FromBody] TournamentParticipantDto participantDto, CancellationToken cancellationToken)
        {
            // El UserId siempre sale del token, nunca del body
            var userIdFromToken = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdFromToken))
            {
                return Unauthorized(new { message = "No se pudo identificar al usuario." });
            }

            try
            {
                var safeDto = participantDto with { UserId = userIdFromToken };

                var success = await _tournamentService.RegisterParticipantAsync(id, safeDto, cancellationToken);
                if (!success)
                {
                    return BadRequest(new { message = "No se pudo realizar la inscripción. Es posible que el torneo esté lleno, ya haya comenzado, que falte habilitar la inscripción (30 min antes) o ya estés inscrito." });
                }

                return Ok(new { message = "¡Inscripción exitosa al torneo!" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // Endpoint para inscribir un equipo a un torneo por equipos
        [HttpPost("{id}/register-team")]
        [Authorize]
        public async Task<IActionResult> RegisterTeam(string id, [FromBody] TeamTournamentRegisterRequest request, CancellationToken cancellationToken)
        {
            var userIdFromToken = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdFromToken))
            {
                return Unauthorized(new { message = "No se pudo identificar al usuario." });
            }

            if (string.IsNullOrWhiteSpace(request.TeamId))
            {
                return BadRequest(new { message = "Debés indicar el equipo a inscribir." });
            }

            try
            {
                var success = await _tournamentService.RegisterTeamAsync(id, request.TeamId, userIdFromToken, cancellationToken);
                if (!success)
                {
                    return BadRequest(new { message = "No se pudo realizar la inscripción del equipo. Verificá cupo, tipo de torneo, ventana de inscripción (30 min antes) o que ya esté inscrito." });
                }

                return Ok(new { message = "¡Equipo inscripto exitosamente al torneo!" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // RF-17: modificar torneo (solo moderadores)
        [HttpPut("{id}")]
        [Authorize(Roles = AppRoles.Staff)]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateTournamentRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var updated = await _tournamentService.UpdateTournamentAsync(id, request, cancellationToken);
                if (updated == null) return NotFound(new { message = "Torneo no encontrado." });
                return Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // RF-18: cancelar torneo (solo moderadores)
        [HttpPost("{id}/cancel")]
        [Authorize(Roles = AppRoles.Staff)]
        public async Task<IActionResult> Cancel(string id, CancellationToken cancellationToken)
        {
            try
            {
                var cancelled = await _tournamentService.CancelTournamentAsync(id, cancellationToken);
                if (!cancelled) return NotFound(new { message = "Torneo no encontrado." });
                return Ok(new { message = "Torneo cancelado." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // RF-22: verificar rango de un participante (solo moderadores)
        [HttpPost("{id}/participants/{userId}/verify-rank")]
        [Authorize(Roles = AppRoles.Staff)]
        public async Task<IActionResult> VerifyRank(string id, string userId, [FromBody] VerifyRankRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var verifier = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "moderador";
                var ok = await _tournamentService.VerifyParticipantRankAsync(id, userId, request.Verified, verifier, cancellationToken);
                if (!ok) return NotFound(new { message = "Torneo o participante no encontrado." });
                return Ok(new { message = request.Verified ? "Rango verificado." : "Verificación de rango rechazada." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // Endpoint para eliminar un torneo (solo moderadores)
        [HttpDelete("{id}")]
        [Authorize(Roles = AppRoles.Staff)]
        public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
        {
            var deleted = await _tournamentService.DeleteTournamentAsync(id, cancellationToken);
            if (!deleted) return NotFound(new { message = "Torneo no encontrado." });

            return Ok(new { message = "Torneo eliminado con éxito." });
        }

        // Endpoint para ver las llaves (enfrentamientos) de un torneo
        [HttpGet("{id}/matches")]
        public async Task<ActionResult<List<MatchResponse>>> GetMatches(string id, CancellationToken cancellationToken)
        {
            var matches = await _tournamentService.GetMatchesAsync(id, cancellationToken);
            if (matches == null) return NotFound(new { message = "Torneo no encontrado." });
            return Ok(matches);
        }

        // Endpoint para generar las llaves a pedido (solo moderadores)
        [HttpPost("{id}/bracket")]
        [Authorize(Roles = AppRoles.Staff)]
        public async Task<IActionResult> GenerateBracket(string id, CancellationToken cancellationToken)
        {
            try
            {
                var matches = await _tournamentService.GenerateBracketAsync(id, cancellationToken);
                if (matches == null) return NotFound(new { message = "Torneo no encontrado." });
                return Ok(matches);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // Endpoint para registrar o corregir el resultado de un enfrentamiento (solo moderadores)
        [HttpPut("{id}/matches/{matchId}/result")]
        [Authorize(Roles = AppRoles.Staff)]
        public async Task<IActionResult> SetMatchResult(string id, string matchId, [FromBody] MatchResultRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var matches = await _tournamentService.SetMatchResultAsync(id, matchId, request, cancellationToken);
                if (matches == null) return NotFound(new { message = "Torneo o enfrentamiento no encontrado." });
                return Ok(matches);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // RF-28: avanza manualmente al ganador de un partido finalizado (solo moderadores)
        [HttpPost("{id}/matches/{matchId}/advance")]
        [Authorize(Roles = AppRoles.Staff)]
        public async Task<IActionResult> AdvanceWinner(string id, string matchId, CancellationToken cancellationToken)
        {
            try
            {
                var matches = await _tournamentService.AdvanceWinnerAsync(id, matchId, cancellationToken);
                if (matches == null) return NotFound(new { message = "Torneo o enfrentamiento no encontrado." });
                return Ok(matches);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // Endpoint para sumar o restar un punto (ronda, gol, etc.) en un partido (solo moderadores)
        [HttpPost("{id}/matches/{matchId}/score")]
        [Authorize(Roles = AppRoles.Staff)]
        public async Task<IActionResult> AdjustMatchScore(string id, string matchId, [FromBody] MatchScoreRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var matches = await _tournamentService.AdjustMatchScoreAsync(id, matchId, request, cancellationToken);
                if (matches == null) return NotFound(new { message = "Torneo o enfrentamiento no encontrado." });
                return Ok(matches);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // Endpoint para descalificar a un participante (solo moderadores)
        [HttpPost("{id}/participants/{userId}/disqualify")]
        [Authorize(Roles = AppRoles.Staff)]
        public async Task<IActionResult> Disqualify(string id, string userId, [FromBody] DisqualifyRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var matches = await _tournamentService.DisqualifyParticipantAsync(id, userId, request.Reason, cancellationToken);
                if (matches == null) return NotFound(new { message = "Torneo o participante no encontrado." });
                return Ok(matches);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}