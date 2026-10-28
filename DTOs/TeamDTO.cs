using System.ComponentModel.DataAnnotations;

namespace ProyectAPI.DTOs
{
    public class TeamCreateDto
    {
        [Required(ErrorMessage = "El nombre del equipo es obligatorio.")]
        [StringLength(40, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "El tag del equipo es obligatorio.")]
        [StringLength(6, MinimumLength = 2)]
        public string Tag { get; set; } = string.Empty;

        public string LogoUrl { get; set; } = string.Empty;

        [Required(ErrorMessage = "El videojuego es obligatorio.")]
        [RegularExpression("^(League of Legends|Valorant|CS:GO|Rocket League)$",
            ErrorMessage = "El juego no es válido. Opciones permitidas: League of Legends, Valorant, CS:GO, Rocket League.")]
        public string GameName { get; set; } = string.Empty;
    }
}