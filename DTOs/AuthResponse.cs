namespace ProyectAPI.DTOs;

public sealed record AuthResponse(
    string Token,
    UserResponse User
);