using System.ComponentModel.DataAnnotations;

namespace ProyectAPI.DTOs;

// Datos que el propio usuario puede modificar de su perfil (RF-03).
// Los campos vacíos o nulos se dejan como estaban.
public sealed class UpdateProfileRequest
{
    [StringLength(30, MinimumLength = 3, ErrorMessage = "El nombre de usuario debe tener entre 3 y 30 caracteres.")]
    public string? Username { get; set; }

    [EmailAddress(ErrorMessage = "El email no tiene un formato válido.")]
    public string? Email { get; set; }

    public string? AvatarUrl { get; set; }
}
