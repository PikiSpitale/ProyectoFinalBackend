namespace ProyectAPI.Models
{
    public class TeamMember
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = "Member"; // Ej: Roster, Analista, etc.
        public bool IsStarter { get; set; } = true;  // True = Titular, False = Suplente (RF-15)
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastActive { get; set; } = DateTime.UtcNow;
    }
}