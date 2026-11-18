using MongoDB.Bson.Serialization.Attributes;
namespace ProyectAPI.Models;

public sealed class EloAdjustment
{
    [BsonId] public string Id { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string GameName { get; set; } = "";
    public string TournamentId { get; set; } = "";
    public string MatchId { get; set; } = "";
    public int Round { get; set; }
    public int Position { get; set; }
    public int Before { get; set; }
    public int After { get; set; }
    public double OpponentRating { get; set; }
    public List<string> OpponentEntityIds { get; set; } = new();
    public double ExpectedScore { get; set; }
    public int Result { get; set; }
    public int RuleVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
}
