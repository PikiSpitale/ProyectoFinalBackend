using ProyectAPI.DTOs;

namespace ProyectAPI.Services;

public interface IAuthService
{
    Task<UserResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken
    );
}
