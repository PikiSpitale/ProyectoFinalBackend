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
    private readonly ITournamentRepository _tournamentRepository;

    public ProfileController(IUserRepository userRepository, ITournamentRepository tournamentRepository)
    {
        _userRepository = userRepository;
        _tournamentRepository = tournamentRepository;
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

    // RF-03 / RF-04: el usuario modifica su nombre, email y avatar.
    // El email y el nombre de usuario no pueden quedar repetidos en otra cuenta.
    [HttpPut("me")]
    public async Task<ActionResult> UpdateMyProfile(
        [FromBody] UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null) return NotFound(new { message = "Usuario no encontrado." });

        var newUsername = string.IsNullOrWhiteSpace(request.Username)
            ? user.Username
            : request.Username.Trim();

        var newEmail = string.IsNullOrWhiteSpace(request.Email)
            ? user.Email
            : request.Email.Trim().ToLowerInvariant();

        if (newUsername.Length < 3)
        {
            return BadRequest(new { message = "El nombre de usuario debe tener al menos 3 caracteres." });
        }

        if (!string.Equals(newUsername, user.Username, StringComparison.Ordinal))
        {
            var existingUsername = await _userRepository.GetByUsernameAsync(newUsername, cancellationToken);
            if (existingUsername is not null && existingUsername.Id != user.Id)
            {
                return Conflict(new { message = "Ese nombre de usuario ya está en uso." });
            }
        }

        if (!string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var existingEmail = await _userRepository.GetByEmailAsync(newEmail, cancellationToken);
            if (existingEmail is not null && existingEmail.Id != user.Id)
            {
                return Conflict(new { message = "Ese email ya está asociado a otra cuenta." });
            }
        }

        user.Username = newUsername;
        user.Email = newEmail;

        // Si el campo viene nulo se conserva el avatar actual; si viene vacío se borra.
        if (request.AvatarUrl is not null)
        {
            user.AvatarUrl = request.AvatarUrl.Trim();
        }

        await _userRepository.UpdateAsync(user, cancellationToken);

        return Ok(new
        {
            message = "Perfil actualizado con éxito",
            Id = user.Id,
            user.Username,
            user.Email,
            AvatarUrl = user.AvatarUrl
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

        // RF-08: no se puede desvincular un juego con un torneo en curso, porque
        // el usuario está compitiendo y su inscripción quedaría inválida.
        if (await _tournamentRepository.HasInProgressTournamentWithGameAsync(gameToRemove.GameName, userId, cancellationToken))
        {
            return BadRequest(new
            {
                message = "No podés eliminar este videojuego porque tenés un torneo en curso de ese juego. Esperá a que finalice."
            });
        }

        user.Games.Remove(gameToRemove);
        await _userRepository.UpdateAsync(user, cancellationToken);

        // RF-08: se limpian las inscripciones a torneos que todavía no comenzaron.
        await _tournamentRepository.RemoveParticipantFromUpcomingTournamentsAsync(
            gameToRemove.GameName, userId, cancellationToken);

        return Ok(new { message = "Videojuego eliminado exitosamente", games = user.Games });
    }

    // ==========================================
    // ESTADÍSTICAS Y AVATAR
    // ==========================================

    // Las estadísticas se actualizan desde los resultados de torneos (RF-36). No se permite actualización libre por el usuario.
    [HttpPut("stats")]
    [Authorize(Roles = AppRoles.Staff)]
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