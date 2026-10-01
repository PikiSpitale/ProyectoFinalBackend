using System.ComponentModel.DataAnnotations;

namespace ProyectAPI.DTOs;

public sealed class RegisterRequest
{
    [Required]
    [StringLength(30, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(120)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    // NUEVO: Lista opcional de videojuegos al registrarse
    public List<AddGameRequest>? Games { get; set; }
}