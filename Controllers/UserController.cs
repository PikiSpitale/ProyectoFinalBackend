using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using ProyectAPI.Models;
using System.Security.Claims;

namespace ProyectAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IMongoCollection<User> _usersCollection;

    public UsersController(IMongoDatabase database)
    {
        _usersCollection = database.GetCollection<User>("Users"); // Asegúrate de que el nombre de tu colección sea "Users" o el que uses
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken)
    {
        // Obtenemos el ID del usuario autenticado desde los Claims del JWT
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "Token inválido o ausente." });
        }

        // Buscamos al usuario en MongoDB
        var user = await _usersCollection.Find(u => u.Id == userId).FirstOrDefaultAsync(cancellationToken);
        if (user == null)
        {
            return NotFound(new { message = "Usuario no encontrado." });
        }

        // Retornamos un objeto limpio con la información que necesita el perfil en React
        return Ok(new
        {
            username = user.Username,
            createdAt = user.CreatedAt,
            games = user.Games,
            stats = user.Stats
        });
    }
}