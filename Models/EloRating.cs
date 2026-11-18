using MongoDB.Bson.Serialization.Attributes;
namespace ProyectAPI.Models;

public sealed class EloRating
{
    [BsonId] public string Id { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Name { get; set; } = "";
    public string NameSort { get; set; } = "";
    public string GameName { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Rating { get; set; } = 1000;
    public int MatchesPlayed { get; set; }
    public int MatchesWon { get; set; }
    public int MatchesLost { get; set; }
}
