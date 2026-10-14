using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProyectAPI.DTOs;
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

        // Endpoint para crear un torneo con validación de fechas
        [HttpPost]
        [Authorize]
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
                return BadRequest(new { message = "No se pudo realizar la inscripción. Es posible que el torneo esté lleno o ya estés inscrito." });
            }

            return Ok(new { message = "¡Inscripción exitosa al torneo!" });
        }

        // Endpoint para eliminar un torneo
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
        {
            var deleted = await _tournamentService.DeleteTournamentAsync(id, cancellationToken);
            if (!deleted) return NotFound(new { message = "Torneo no encontrado." });

            return Ok(new { message = "Torneo eliminado con éxito." });
        }
    }
}