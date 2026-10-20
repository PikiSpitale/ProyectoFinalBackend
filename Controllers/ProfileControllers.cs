using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
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
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null) return NotFound("Usuario no encontrado.");

        user.LastActive = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user, cancellationToken);

        return Ok(new
        {
            Id = user.Id,
            user.Username,
            user.Email,
            user.Role,
            user.CreatedAt,
            user.Games,
            user.Stats,
            AvatarUrl = user.AvatarUrl,
            user.LastActive
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult> GetUserProfile(string id, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return NotFound(new { message = "Usuario no encontrado." });
        }

        var user = await _userRepository.GetByIdAsync(id, cancellationToken);
        if (user == null)
        {
            return NotFound(new { message = "Usuario no encontrado." });
        }

        return Ok(new
        {
            Id = user.Id,
            user.Username,
            user.CreatedAt,
            user.Games,
            user.Stats,
            AvatarUrl = user.AvatarUrl,
            user.LastActive
        });
    }

    // ==========================================
    // GESTIÓN DE VIDEOJUEGOS (RF-05, RF-06, RF-07, RF-08)
    // ==========================================

    // RF-06: Agregar videojuego al perfil
    [HttpPost("games")]
    public async Task<ActionResult> AddGameToProfile(
        [FromBody] AddGameRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null) return NotFound("Usuario no encontrado.");

        // Evitar duplicados del mismo juego
        if (user.Games.Any(g => g.GameName.Equals(request.GameName, StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(new { message = "Ya tienes este videojuego asociado a tu perfil." });
        }

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

    // RF-07: Modificar el nombre de usuario y rango de un videojuego asociado
    [HttpPut("games")]
    public async Task<ActionResult> UpdateGameInProfile(
        [FromBody] AddGameRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null) return NotFound("Usuario no encontrado.");

        var existingGame = user.Games.FirstOrDefault(g => g.GameName.Equals(request.GameName, StringComparison.OrdinalIgnoreCase));
        if (existingGame == null)
        {
            return NotFound(new { message = "No tienes registrado este videojuego en tu perfil." });
        }

        existingGame.InGameName = request.InGameName;
        existingGame.Rank = request.Rank;

        await _userRepository.UpdateAsync(user, cancellationToken);

        return Ok(new { message = "Videojuego actualizado exitosamente", games = user.Games });
    }

    // RF-08: Eliminar un videojuego asociado al perfil
    [HttpDelete("games/{gameName}")]
    public async Task<ActionResult> RemoveGameFromProfile(
        string gameName,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null) return NotFound("Usuario no encontrado.");

        var gameToRemove = user.Games.FirstOrDefault(g => g.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (gameToRemove == null)
        {
            return NotFound(new { message = "El videojuego no se encuentra en tu perfil." });
        }

        user.Games.Remove(gameToRemove);
        await _userRepository.UpdateAsync(user, cancellationToken);

        return Ok(new { message = "Videojuego eliminado exitosamente", games = user.Games });
    }

    // ==========================================
    // ESTADÍSTICAS Y AVATAR
    // ==========================================

    [HttpPut("stats")]
    public async Task<ActionResult> UpdateUserStats(
        [FromBody] UpdateStatsRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null) return NotFound("Usuario no encontrado.");

        user.Stats.Wins = request.Wins;
        user.Stats.Losses = request.Losses;
        user.Stats.Draws = request.Draws;
        user.Stats.MatchesPlayed = user.Stats.Wins + user.Stats.Losses + user.Stats.Draws;

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