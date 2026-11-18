namespace ProyectAPI.Models;

public sealed class TournamentEnrollment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? TeamId { get; set; }
    public string? TeamName { get; set; }
    public string Status { get; set; } = "Pending";
    public List<TournamentPayer> Players { get; set; } = new();
}
public sealed class TournamentPayer
{
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string InGameName { get; set; } = "";
    public string Rank { get; set; } = "";
    public bool Confirmed { get; set; }
    public string? DebitId { get; set; }
}
