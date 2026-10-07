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

        [HttpGet]
        public async Task<ActionResult<List<TournamentResponse>>> GetAll(CancellationToken cancellationToken)
        {
            var tournaments = await _tournamentService.GetAllTournamentsAsync(cancellationToken);
            return Ok(tournaments);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<TournamentResponse>> GetById(string id, CancellationToken cancellationToken)
        {
            var tournament = await _tournamentService.GetTournamentByIdAsync(id, cancellationToken);
            if (tournament == null) return NotFound(new { message = "Torneo no encontrado." });
            return Ok(tournament);
        }

        [HttpPost]
        [Authorize] // Opcional: solo administradores o usuarios logueados según prefieras
        public async Task<IActionResult> Create([FromBody] CreateTournamentRequest request, CancellationToken cancellationToken)
        {
            await _tournamentService.CreateTournamentAsync(request, cancellationToken);
            return Ok(new { message = "Torneo creado con éxito." });
        }

        [HttpPost("{id}/register")]
        [Authorize]
        public async Task<IActionResult> Register(string id, [FromBody] TournamentParticipantDto participantDto, CancellationToken cancellationToken)
        {
            // Opcional: puedes extraer el UserId directamente del Token JWT si lo prefieres por seguridad:
            var userIdFromToken = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var success = await _tournamentService.RegisterParticipantAsync(id, participantDto, cancellationToken);
            if (!success)
            {
                return BadRequest(new { message = "No se pudo realizar la inscripción. Es posible que el torneo esté lleno o ya estés inscrito." });
            }

            return Ok(new { message = "¡Inscripción exitosa al torneo!" });
        }
    }
}