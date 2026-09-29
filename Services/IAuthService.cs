using ProyectAPI.DTOs;

namespace ProyectAPI.Services;

public interface IAuthService
{
    Task<UserResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken
    );
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
}
