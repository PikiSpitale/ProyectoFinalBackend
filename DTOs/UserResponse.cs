namespace ProyectAPI.DTOs;

public sealed record UserResponse(
    string Id,
    string Username,
    string Email,
    string Role,
    DateTime CreatedAt
);
