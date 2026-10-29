using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Repositories;
using ProyectAPI.Services;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace ProyectAPI.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private const string CookieName = "access_token";

    private readonly IAuthService _authService;
    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _configuration;

    public AuthController(IAuthService authService, IUserRepository userRepository, IConfiguration configuration)
    {
        _authService = authService;
        _userRepository = userRepository;
        _configuration = configuration;
    }

    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        RegisterRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var authResponse = await _authService.RegisterAsync(request, cancellationToken);
            SetAuthCookie(authResponse.Token);

            // El token viaja solo en la cookie HttpOnly, no en el JSON
            return StatusCode(StatusCodes.Status201Created, new { user = authResponse.User });
        }
        catch (DuplicateUserException)
        {
            return Conflict(new
            {
                message = "El email o nombre de usuario ya está registrado."
            });
        }
    }

    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        LoginRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var authResponse = await _authService.LoginAsync(request, cancellationToken);
            SetAuthCookie(authResponse.Token);

            return Ok(new { user = authResponse.User });
        }
        catch (InvalidCredentialsException)
        {
            return Unauthorized(new { message = "Correo o contraseña incorrectos." });
        }
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        // Las opciones deben coincidir con las de la cookie original para que el navegador la borre
        Response.Cookies.Delete(CookieName, new CookieOptions
        {
            Secure = true,
            SameSite = SameSiteMode.None
        });

        return Ok(new { message = "Sesión cerrada." });
    }

    [Authorize]
    [HttpGet("me")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        var user = await _authService.GetCurrentUserAsync(userId, cancellationToken);
        return user == null ? Unauthorized() : Ok(new { user });
    }

    private void SetAuthCookie(string token)
    {
        Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,                        // JavaScript no puede leerla (protege contra XSS)
            Secure = true,                          // solo viaja por HTTPS
            SameSite = SameSiteMode.None,           // frontend (http://localhost:5173) y API (https://localhost:7039) son orígenes distintos
            Expires = DateTimeOffset.UtcNow.AddDays(7)  // igual que la expiración del JWT
        });
    }

    [HttpGet("external/google")]
    public IActionResult GoogleLogin()
    {
        var redirectUrl = Url.Action("GoogleCallback", "Auth", null, Request.Scheme);
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    // El callback de Google lo procesa el middleware; esta ruta completa el registro.
    [HttpGet("external/google/complete")]
    public async Task<IActionResult> GoogleCallback(CancellationToken cancellationToken)
    {
        var result = await HttpContext.AuthenticateAsync("Cookies");
        if (!result.Succeeded || result.Principal == null)
        {
            try { await HttpContext.SignOutAsync("Cookies"); } catch { }
            return Redirect("http://localhost:5173/login?error=external_auth_failed");
        }

        var claims = result.Principal.Claims.ToList();
        var email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value?.ToLowerInvariant();
        var externalId = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        var name = claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
        var picture = claims.FirstOrDefault(c => c.Type == "picture")?.Value ?? claims.FirstOrDefault(c => c.Type == "urn:google:picture")?.Value;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(externalId))
        {
            await HttpContext.SignOutAsync("Cookies");
            return Redirect("http://localhost:5173/login?error=missing_email");
        }

        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);
        if (user == null)
        {
            var baseUsername = (name ?? email.Split('@')[0]).Replace(" ", "");
            if (string.IsNullOrWhiteSpace(baseUsername)) baseUsername = "user";
            var username = baseUsername;
            var counter = 0;
            while (await _userRepository.ExistsByUsernameAsync(username, cancellationToken))
            {
                counter++;
                username = $"{baseUsername}{counter}";
                if (counter > 20) break;
            }

            user = new User
            {
                Username = username,
                Email = email,
                PasswordHash = string.Empty,
                Role = "User",
                CreatedAt = DateTime.UtcNow,
                AvatarUrl = picture ?? string.Empty,
                AuthProvider = "Google",
                ExternalId = externalId,
                EmailVerified = true,
                Games = new List<UserGame>(),
                Stats = new UserStats { MatchesPlayed = 0, Winrate = 0 }
            };

            await _userRepository.CreateAsync(user, cancellationToken);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(user.ExternalId))
            {
                user.ExternalId = externalId;
                user.AuthProvider = string.IsNullOrWhiteSpace(user.AuthProvider) || user.AuthProvider == "Local" ? "Google" : user.AuthProvider;
                if (!string.IsNullOrWhiteSpace(picture) && string.IsNullOrWhiteSpace(user.AvatarUrl))
                    user.AvatarUrl = picture;
                user.EmailVerified = true;
                await _userRepository.UpdateAsync(user, cancellationToken);
            }
        }

        var token = GenerateJwtToken(user);
        SetAuthCookie(token);

        await HttpContext.SignOutAsync("Cookies");

        return Redirect("http://localhost:5173/dashboard");
    }

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
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(ClaimTypes.Name, user.Username)
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
