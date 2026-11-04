using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ProyectAPI.Models;

public sealed class TeamInvitation
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string TeamId { get; set; } = "";
    public string TeamName { get; set; } = "";
    public string SenderId { get; set; } = "";
    public string SenderUsername { get; set; } = "";
    public string RecipientId { get; set; } = "";
    public string RecipientUsername { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
