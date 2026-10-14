namespace ProyectAPI.Models
{
    public class TeamMember
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = "Member";
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        // Propiedad para el sistema de estado online/offline
        public DateTime LastActive { get; set; } = DateTime.UtcNow;
    }
}