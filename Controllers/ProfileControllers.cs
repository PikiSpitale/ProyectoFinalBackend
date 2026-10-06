using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Repositories;

namespace ProyectAPI.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize] // Protege todo el controlador: solo entra quien tiene un Token JWT válido
public sealed class ProfileController : ControllerBase
{
    private readonly IUserRepository _userRepository;

    public ProfileController(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    [HttpGet("me")]
    public async Task<ActionResult> GetMyProfile(CancellationToken cancellationToken)
    {
        // Leemos el ID del usuario directamente desde los claims del Token JWT
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        // Buscamos al usuario en la base de datos de MongoDB
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null) return NotFound("Usuario no encontrado.");

        // Retornamos los datos limpios que consumirá React (incluyendo juegos, avatar y estadísticas)
        return Ok(new
        {
            user.Username,
            user.Email,
            user.Role,
            user.CreatedAt,
            user.Games,
            user.Stats,
            AvatarUrl = user.AvatarUrl
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

    [HttpPut("stats")]
    public async Task<ActionResult> UpdateUserStats(
        [FromBody] UpdateStatsRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null) return NotFound("Usuario no encontrado.");

        // Asignamos directamente los valores ingresados (evita la acumulación infinita)
        user.Stats.Wins = request.Wins;
        user.Stats.Losses = request.Losses;
        user.Stats.Draws = request.Draws;

        // Calculamos el total de partidas jugadas
        user.Stats.MatchesPlayed = user.Stats.Wins + user.Stats.Losses + user.Stats.Draws;

        // Calculamos el Winrate automáticamente evitando división por cero
        if (user.Stats.MatchesPlayed > 0)
        {
            user.Stats.Winrate = (int)Math.Round((double)user.Stats.Wins / user.Stats.MatchesPlayed * 100);
        }
        else
        {
            user.Stats.Winrate = 0;
        }

        await _userRepository.UpdateAsync(user, cancellationToken);

        return Ok(new { message = "Estadísticas actualizadas con éxito", stats = user.Stats });
    }

    [HttpPut("avatar")]
    public async Task<ActionResult> UpdateAvatar(
        [FromBody] UpdateAvatarRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null) return NotFound("Usuario no encontrado.");

        user.AvatarUrl = request.AvatarUrl ?? string.Empty;
        await _userRepository.UpdateAsync(user, cancellationToken);

        return Ok(new { message = "Avatar actualizado con éxito", avatarUrl = user.AvatarUrl });
    }
}