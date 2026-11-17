using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
namespace ProyectAPI.Models;

public sealed class Wallet
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = "";
    public long Balance { get; set; }
    public DateTime UpdatedAt { get; set; }
}
