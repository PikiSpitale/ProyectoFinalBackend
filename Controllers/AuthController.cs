using Microsoft.AspNetCore.Mvc;
using ProyectAPI.DTOs;
using ProyectAPI.Services;

namespace ProyectAPI.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private const string CookieName = "access_token";

    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
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
}