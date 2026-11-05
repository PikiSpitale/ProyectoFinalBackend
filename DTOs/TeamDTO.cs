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
        [StringLength(60)]
        public string GameName { get; set; } = string.Empty;

        // "InviteOnly" | "Open"
        public string? JoinPolicy { get; set; }
        // "None" | "Fixed" | "Minimum" (solo si Open)
        public string? RankFilterType { get; set; }
        // Rango fijo o mínimo
        public string? RankFilterValue { get; set; }
    }

    // RF-11
    public class InviteTeamMemberDto
    {
        [Required(ErrorMessage = "El nombre de usuario a invitar es obligatorio.")]
        public string Username { get; set; } = string.Empty;
    }

    // RF-12
    public class RespondTeamInvitationDto
    {
        // "Accept" o "Reject"
        [Required]
        public string Response { get; set; } = string.Empty;
    }

    public class TeamInvitationDto
    {
        public string Id { get; set; } = string.Empty;
        public string TeamId { get; set; } = string.Empty;
        public string TeamName { get; set; } = string.Empty;
        public string TeamTag { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
        public string InviterId { get; set; } = string.Empty;
        public string InviterUsername { get; set; } = string.Empty;
        public string InviteeId { get; set; } = string.Empty;
        public string InviteeUsername { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class UserSearchResultDto
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
    }
}