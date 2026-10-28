using System.ComponentModel.DataAnnotations;

namespace ProyectAPI.DTOs;

public sealed class LoginRequest
{
    [EmailAddress(ErrorMessage = "Formato de correo inválido.")]
    public string? Email { get; set; }

    public string? Username { get; set; }

    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;
}