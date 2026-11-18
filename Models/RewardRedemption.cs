using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
namespace ProyectAPI.Models;

public sealed class RewardRedemption
{
    [BsonId] public string Id { get; set; } = "";
    [BsonRepresentation(BsonType.ObjectId)] public string UserId { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public RewardProduct Product { get; set; } = null!;
    public long CostPoints { get; set; }
    public string DebitId { get; set; } = "";
    public string Code { get; set; } = "";
    public string Disclaimer { get; set; } = "Test code. Not valid for redemption on any platform.";
    public bool IsSimulation { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
