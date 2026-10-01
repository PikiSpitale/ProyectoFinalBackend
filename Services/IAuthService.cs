using ProyectAPI.DTOs;

namespace ProyectAPI.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken
    );
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
}