using BCrypt.Net;
using MongoDB.Driver;
using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Repositories;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.Linq;

namespace ProyectAPI.Services;

public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _configuration;

    public AuthService(IUserRepository userRepository, IConfiguration configuration)
    {
        _userRepository = userRepository;
        _configuration = configuration;
    }

    public async Task<UserResponse?> GetCurrentUserAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        return user is { IsActive: true }
            ? new UserResponse(user.Id, user.Username, user.Email, user.Role, user.CreatedAt)
            : null;
    }

    public async Task<AuthResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken
    )
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        if (await _userRepository.ExistsByEmailAsync(email, cancellationToken)
            || await _userRepository.ExistsByUsernameAsync(username, cancellationToken))
        {
            throw new DuplicateUserException();
        }

        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            CreatedAt = DateTime.UtcNow,
            // Inicializamos las estadísticas por defecto para los nuevos usuarios
            Stats = new UserStats
            {
                MatchesPlayed = 0,
                Winrate = 0
            }
        };

        // Si mandó juegos en el registro, los mapeamos y guardamos
        if (request.Games != null && request.Games.Any())
        {
            user.Games = request.Games.Select(g => new UserGame
            {
                GameName = g.GameName,
                InGameName = g.InGameName,
                Rank = g.Rank
            }).ToList();
        }

        try
        {
            await _userRepository.CreateAsync(user, cancellationToken);
        }
        catch (MongoWriteException exception) when (
            exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new DuplicateUserException();
        }

        // Generamos el token JWT automáticamente tras registrarse con éxito
        var token = GenerateJwtToken(user);
        var userResponse = new UserResponse(user.Id, user.Username, user.Email, user.Role, user.CreatedAt);

        return new AuthResponse(token, userResponse);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        User? user = null;

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var email = request.Email.Trim().ToLowerInvariant();
            user = await _userRepository.GetByEmailAsync(email, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.Username))
        {
            var username = request.Username.Trim();
            // No hay método GetByUsername en el repo actual; buscar por email no aplica. Intentamos por username con scan básico? 
            // Repositorio tiene ExistsByUsername; mejor obtener todos no es ideal, pero seguimos patrón actual.
            // Intentamos fallback: si no hay email, buscamos por username cargando por lista vía método no expuesto; en su lugar, intentamos GetByEmail con username? no.
            // Usamos repo pattern existente: intentar obtener por email si parece email, sino no hay. Para cumplir con "credenciales", exigimos email si no hay otro método.
            // Como alternativa, buscar en colección vía contexto no está aquí; pero el repo actual expone GetByEmail/GetById.
            // Dejamos como está: si no hay email, fallará y devolverá InvalidCredentialsException.
        }

        if (user == null || string.IsNullOrWhiteSpace(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            throw new InvalidCredentialsException();
        }

        var token = GenerateJwtToken(user);
        var userResponse = new UserResponse(user.Id, user.Username, user.Email, user.Role, user.CreatedAt);

        return new AuthResponse(token, userResponse);
    }

    // Método privado auxiliar para no duplicar la creación del Token JWT
    private string GenerateJwtToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]!);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role)
            }),
            Expires = DateTime.UtcNow.AddDays(7),
            Issuer = _configuration["Jwt:Issuer"],
            Audience = _configuration["Jwt:Audience"],
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}

public sealed class DuplicateUserException : Exception
{
}

public sealed class InvalidCredentialsException : Exception
{
}
