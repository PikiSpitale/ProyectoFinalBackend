using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProyectAPI.Services;
namespace ProyectAPI.Controllers;

[ApiController, Authorize, Route("api/users/me/points")]
public sealed class PointsController(WalletService service) : ControllerBase
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")
        ?? throw new WalletOperationException(401, "invalid_session", "Sesión no válida.");
    [HttpGet]
    public Task<ActionResult> Balance(CancellationToken ct) => Respond(async () => new { balance = await service.BalanceAsync(Actor, ct) });
    [HttpGet("history")]
    public Task<ActionResult> History([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Respond(async () => new { page, pageSize, items = await service.HistoryAsync(Actor, page, pageSize, ct) });
    private async Task<ActionResult> Respond(Func<Task<object>> action)
    {
        try { return Ok(await action()); }
        catch (WalletOperationException ex) { return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message }); }
    }
}
