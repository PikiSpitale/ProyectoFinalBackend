using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Services;

namespace ProyectAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TeamsController : ControllerBase
    {
        private readonly TeamService _teamService;

        public TeamsController(TeamService teamService)
        {
            _teamService = teamService;
        }

        // Endpoint para obtener todos los equipos
        [HttpGet]
        public async Task<ActionResult<List<Team>>> GetAll(CancellationToken cancellationToken)
        {
            var teams = await _teamService.GetAllTeamsAsync(cancellationToken);
            return Ok(teams);
        }

        // Endpoint para obtener un equipo por ID
        [HttpGet("{id}")]
        public async Task<ActionResult<Team>> GetById(string id, CancellationToken cancellationToken)
        {
            var team = await _teamService.GetTeamByIdAsync(id, cancellationToken);
            if (team == null) return NotFound(new { message = "Equipo no encontrado" });
            return Ok(team);
        }

        // Endpoint para crear un equipo
        [HttpPost]
        [Authorize]
        public async Task<ActionResult<Team>> Create([FromBody] TeamCreateDto dto, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            var username = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue("unique_name");

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "Token inválido o expirado" });
            }

            try
            {
                var createdTeam = await _teamService.CreateTeamAsync(dto, userId, username ?? "Capitán", cancellationToken);
                return CreatedAtAction(nameof(GetById), new { id = createdTeam.Id }, createdTeam);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // Endpoint para unirse a un equipo
        [HttpPost("{id}/join")]
        [Authorize]
        public async Task<ActionResult> JoinTeam(string id, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            var username = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue("unique_name");

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "Token inválido o expirado" });
            }

            try
            {
                var success = await _teamService.JoinTeamAsync(id, userId, username ?? "Jugador", cancellationToken);
                if (!success) return NotFound(new { message = "Equipo no encontrado" });

                return Ok(new { message = "¡Te has unido al equipo con éxito!" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // Endpoint para abandonar un equipo
        [HttpPost("{id}/leave")]
        [Authorize]
        public async Task<ActionResult> LeaveTeam(string id, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "Token inválido o expirado" });
            }

            try
            {
                var success = await _teamService.LeaveTeamAsync(id, userId, cancellationToken);
                if (!success) return BadRequest(new { message = "No se pudo abandonar el equipo." });

                return Ok(new { message = "Has abandonado el equipo exitosamente." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}