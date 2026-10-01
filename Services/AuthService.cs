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
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12)
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
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
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