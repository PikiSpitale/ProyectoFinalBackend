using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
namespace ProyectAPI.Models;

public sealed class PointTransaction
{
    [BsonId] public string Id { get; set; } = "";
    [BsonRepresentation(BsonType.ObjectId)] public string UserId { get; set; } = "";
    public long Amount { get; set; }
    public string Reason { get; set; } = "";
    public string Reference { get; set; } = "";
    public string? ReversesId { get; set; }
    public DateTime CreatedAt { get; set; }
}
