using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProyectAPI.DTOs;
using ProyectAPI.Services;
namespace ProyectAPI.Controllers;

[ApiController, Route("api/rewards")]
public sealed class RewardsController(RewardService service) : ControllerBase
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? throw new WalletOperationException(401, "invalid_session", "No authenticated user.");
    [HttpGet("catalog")]
    public IActionResult Catalog() => Ok(new { isSimulation = true, items = service.Catalog() });
    [Authorize, HttpPost("redemptions")]
    public Task<ActionResult> Redeem(RewardRedemptionRequest request, CancellationToken ct) => Respond(async () => await service.Redeem(Actor, request, ct));
    [Authorize, HttpGet("redemptions")]
    public Task<ActionResult> History([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Respond(async () => new { page, pageSize, items = await service.History(Actor, page, pageSize, ct) });
    private async Task<ActionResult> Respond(Func<Task<object>> action)
    {
        try { return Ok(await action()); }
        catch (WalletOperationException e) { return StatusCode(e.StatusCode, new { code = e.Code, message = e.Message }); }
    }
}
