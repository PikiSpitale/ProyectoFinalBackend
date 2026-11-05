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

        [HttpGet]
        public async Task<ActionResult<List<Team>>> GetAll(CancellationToken cancellationToken)
        {
            var teams = await _teamService.GetAllTeamsAsync(cancellationToken);
            return Ok(teams);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Team>> GetById(string id, CancellationToken cancellationToken)
        {
            var team = await _teamService.GetTeamByIdAsync(id, cancellationToken);
            if (team == null) return NotFound(new { message = "Equipo no encontrado" });
            return Ok(team);
        }

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

        // Eliminar equipo: lo puede hacer el capitán o un moderador/admin
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<ActionResult> DeleteTeam(string id, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "Token inválido o expirado" });
            }

            var isModerator = User.IsInRole("Moderator") || User.IsInRole("Admin");

            try
            {
                var success = await _teamService.DeleteTeamAsync(id, userId, isModerator, cancellationToken);
                if (!success) return NotFound(new { message = "Equipo no encontrado" });

                return Ok(new { message = "Equipo eliminado correctamente." });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // RF-10: Actualizar información del equipo
        [HttpPut("{id}")]
        [Authorize]
        public async Task<ActionResult> UpdateTeam(string id, [FromBody] UpdateTeamDto dto, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var success = await _teamService.UpdateTeamAsync(id, dto, userId, cancellationToken);
                if (!success) return NotFound(new { message = "Equipo no encontrado" });
                return Ok(new { message = "Equipo actualizado correctamente" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // RF-13: Expulsar miembro
        [HttpDelete("{id}/members/{memberUserId}")]
        [Authorize]
        public async Task<ActionResult> RemoveMember(string id, string memberUserId, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var success = await _teamService.RemoveMemberAsync(id, memberUserId, userId, cancellationToken);
                if (!success) return NotFound(new { message = "No se pudo completar la operación" });
                return Ok(new { message = "Integrante eliminado del equipo" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // RF-14 y RF-15: Cambiar rol o estado titular/suplente
        [HttpPut("{id}/members/{memberUserId}")]
        [Authorize]
        public async Task<ActionResult> UpdateMemberRole(string id, string memberUserId, [FromBody] UpdateMemberRoleDto dto, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var success = await _teamService.UpdateMemberRoleAsync(id, memberUserId, dto.Role, dto.IsStarter, userId, cancellationToken);
                if (!success) return NotFound(new { message = "No se pudo actualizar el miembro" });
                return Ok(new { message = "Rol y estado actualizados" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // RF-11: invitar usuario (solo capitán)
        [HttpPost("{id}/invitations")]
        [Authorize]
        public async Task<ActionResult> Invite(string id, [FromBody] InviteTeamMemberDto dto, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            try
            {
                var invitation = await _teamService.InviteMemberAsync(id, dto.Username, userId, cancellationToken);
                return Ok(invitation);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // RF-11: invitaciones pendientes del equipo (solo capitán)
        [HttpGet("{id}/invitations")]
        [Authorize]
        public async Task<ActionResult> GetTeamInvitations(string id, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            try
            {
                var invitations = await _teamService.GetTeamInvitationsAsync(id, userId, cancellationToken);
                return Ok(invitations);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // RF-12: mis invitaciones pendientes
        [HttpGet("invitations/me")]
        [Authorize]
        public async Task<ActionResult> GetMyInvitations(CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            var invitations = await _teamService.GetMyInvitationsAsync(userId, cancellationToken);
            return Ok(invitations);
        }

        // RF-12: aceptar/rechazar invitación
        [HttpPost("invitations/{invitationId}/respond")]
        [Authorize]
        public async Task<ActionResult> RespondInvitation(string invitationId, [FromBody] RespondTeamInvitationDto dto, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            try
            {
                await _teamService.RespondInvitationAsync(invitationId, userId, dto.Response, cancellationToken);
                return Ok(new { message = dto.Response.Equals("Accept", StringComparison.OrdinalIgnoreCase) || dto.Response.Equals("Aceptar", StringComparison.OrdinalIgnoreCase)
                    ? "Invitación aceptada. Ya sos parte del equipo."
                    : "Invitación rechazada." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}