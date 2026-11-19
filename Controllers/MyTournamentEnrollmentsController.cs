using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProyectAPI.Services;
namespace ProyectAPI.Controllers;

[ApiController, Authorize, Route("api/users/me/tournament-enrollments")]
public sealed class MyTournamentEnrollmentsController(FrontendReadService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Get([FromQuery] string? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        try
        {
            var actor = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? throw new WalletOperationException(401, "invalid_session", "No authenticated user.");
            return Ok(await service.Enrollments(actor, status, page, pageSize, ct));
        }
        catch (WalletOperationException e) { return StatusCode(e.StatusCode, new { code = e.Code, message = e.Message }); }
    }
}
