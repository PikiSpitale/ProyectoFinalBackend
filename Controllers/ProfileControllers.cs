using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Repositories;

namespace ProyectAPI.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize] // <-- ¡Protege el endpoint! Solo entra quien tiene Token
public sealed class ProfileController : ControllerBase
{
    private readonly IUserRepository _userRepository;

    public ProfileController(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    [HttpGet]
    public async Task<ActionResult> GetMyProfile(CancellationToken cancellationToken)
    {
        // Leemos el ID del usuario directamente desde el Token JWT
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null) return NotFound("Usuario no encontrado.");

        return Ok(new
        {
            user.Username,
            user.Email,
            user.Role,
            user.CreatedAt,
            user.Games // Su lista de videojuegos
        });
    }

    [HttpPost("games")]
    public async Task<ActionResult> AddGameToProfile(
        [FromBody] AddGameRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null) return NotFound();

        // Creamos el juego manual
        var newGame = new UserGame
        {
            GameName = request.GameName,
            InGameName = request.InGameName,
            Rank = request.Rank
        };

        user.Games.Add(newGame);
        await _userRepository.UpdateAsync(user, cancellationToken);

        return Ok(new { message = "Videojuego agregado exitosamente", games = user.Games });
    }
}