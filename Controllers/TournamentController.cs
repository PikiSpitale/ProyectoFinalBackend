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

        // Endpoint para obtener todos los torneos
        [HttpGet]
        public async Task<ActionResult<List<TournamentResponse>>> GetAll(CancellationToken cancellationToken)
        {
            var tournaments = await _tournamentService.GetAllTournamentsAsync(cancellationToken);
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

        // Endpoint para registrarse en un torneo
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

            var safeDto = participantDto with { UserId = userIdFromToken };

            var success = await _tournamentService.RegisterParticipantAsync(id, safeDto, cancellationToken);
            if (!success)
            {
                return BadRequest(new { message = "No se pudo realizar la inscripción. Es posible que el torneo esté lleno, ya haya comenzado o ya estés inscrito." });
            }

            return Ok(new { message = "¡Inscripción exitosa al torneo!" });
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