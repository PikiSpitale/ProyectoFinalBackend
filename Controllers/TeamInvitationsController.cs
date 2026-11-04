using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProyectAPI.DTOs;
using ProyectAPI.Services;

namespace ProyectAPI.Controllers;

[ApiController, Authorize, TeamExceptionFilter]
public sealed class TeamInvitationsController(TeamService service) : ControllerBase
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")
        ?? throw new TeamOperationException(401, "Sesión no válida.");

    [HttpGet("api/teams/{id}/invitees")]
    public async Task<ActionResult> Search(string id, [FromQuery] string username, CancellationToken ct) =>
        Ok(await service.FindInviteesAsync(id, Actor, username, ct));

    [HttpPost("api/teams/{id}/invitations")]
    public async Task<ActionResult> Invite(string id, [FromBody] InviteTeamDto dto, CancellationToken ct) =>
        StatusCode(201, await service.InviteAsync(id, Actor, dto.RecipientId, ct));

    [HttpGet("api/teams/{id}/invitations")]
    public async Task<ActionResult> Sent(string id, CancellationToken ct) =>
        Ok(await service.ListInvitationsAsync(id, false, Actor, ct));

    [HttpGet("api/team-invitations")]
    public async Task<ActionResult> Received(CancellationToken ct) =>
        Ok(await service.ListInvitationsAsync(Actor, true, Actor, ct));

    [HttpPost("api/team-invitations/{id}/accept")]
    public async Task<ActionResult> Accept(string id, CancellationToken ct) =>
        Ok(await service.ResolveInvitationAsync(id, Actor, "accept", ct));

    [HttpPost("api/team-invitations/{id}/reject")]
    public async Task<ActionResult> Reject(string id, CancellationToken ct) =>
        Ok(await service.ResolveInvitationAsync(id, Actor, "reject", ct));

    [HttpPost("api/team-invitations/{id}/cancel")]
    public async Task<ActionResult> Cancel(string id, CancellationToken ct) =>
        Ok(await service.ResolveInvitationAsync(id, Actor, "cancel", ct));
}
