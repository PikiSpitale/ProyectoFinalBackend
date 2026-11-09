using System.ComponentModel.DataAnnotations;

namespace ProyectAPI.DTOs;

public sealed class AddGameRequest
{
    [Required]
    [RegularExpression("^(League of Legends|Valorant|CS:GO|Rocket League|Counter-Strike 2|EA FC 27)$",
        ErrorMessage = "El juego no es válido. Opciones permitidas: League of Legends, Valorant, CS:GO, Rocket League, Counter-Strike 2, EA FC 27.")]
    public string GameName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre de usuario en el juego es obligatorio.")]
    public string InGameName { get; set; } = string.Empty;

    [Required(ErrorMessage = "El rango es obligatorio.")]
    public string Rank { get; set; } = string.Empty;
}