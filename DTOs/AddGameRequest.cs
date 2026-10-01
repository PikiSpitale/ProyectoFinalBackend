using System.ComponentModel.DataAnnotations;

namespace ProyectAPI.DTOs;

public sealed class AddGameRequest
{
    [Required]
    // Aquí defines exactamente qué juegos acepta tu plataforma separados por un "pipe" (|)
    [RegularExpression("^(League of Legends|Valorant|CS:GO|Rocket League)$",
        ErrorMessage = "El juego no es válido. Opciones permitidas: League of Legends, Valorant, CS:GO, Rocket League.")]
    public string GameName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre de usuario en el juego es obligatorio.")]
    public string InGameName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El rango es obligatorio.")]
    public string Rank { get; set; } = string.Empty;
}