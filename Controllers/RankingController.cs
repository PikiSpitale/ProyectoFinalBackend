using Microsoft.AspNetCore.Mvc;
using ProyectAPI.Services;
namespace ProyectAPI.Controllers;

[ApiController, Route("api/rankings")]
public sealed class RankingsController(RankingService service) : ControllerBase
{
    [HttpGet]
    public Task<ActionResult> GetLeaderboard([FromQuery] string? gameName, CancellationToken ct) => Respond(async () => await service.GetLeaderboardAsync(gameName, ct));
    [HttpGet("players")]
    public Task<ActionResult> Players([FromQuery] string? gameName, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Respond(async () => new { page, pageSize, items = await service.Rankings("player", gameName, page, pageSize, ct) });
    [HttpGet("teams")]
    public Task<ActionResult> Teams([FromQuery] string? gameName, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Respond(async () => new { page, pageSize, items = await service.Rankings("team", gameName, page, pageSize, ct) });
    [HttpGet("players/{entityId}/history")]
    public Task<ActionResult> PlayerHistory(string entityId, [FromQuery] string? gameName, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Respond(async () => new { page, pageSize, items = await service.History("player", entityId, gameName, page, pageSize, ct) });
    [HttpGet("teams/{entityId}/history")]
    public Task<ActionResult> TeamHistory(string entityId, [FromQuery] string? gameName, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Respond(async () => new { page, pageSize, items = await service.History("team", entityId, gameName, page, pageSize, ct) });
    private async Task<ActionResult> Respond(Func<Task<object>> action)
    {
        try { return Ok(await action()); }
        catch (WalletOperationException e) { return StatusCode(e.StatusCode, new { code = e.Code, message = e.Message }); }
    }
}
