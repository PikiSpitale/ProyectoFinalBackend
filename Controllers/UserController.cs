using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProyectAPI.Services;

namespace ProyectAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly TeamService _teamService;

    public UsersController(TeamService teamService)
    {
        _teamService = teamService;
    }

    // Búsqueda mínima para invitar usuarios a equipos (RF-11)
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? query, CancellationToken cancellationToken)
    {
        var results = await _teamService.SearchUsersAsync(query ?? string.Empty, cancellationToken);
        return Ok(results);
    }
}
