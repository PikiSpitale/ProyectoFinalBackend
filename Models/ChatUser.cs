using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ProyectoFinalBackend.Models
{
    public class ChatUser
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; set; }

        [BsonElement("Username")]
        public string Username { get; set; }

        [BsonElement("Role")]
        public string Role { get; set; } = "user";

        [BsonElement("IsActive")]
        public bool IsActive { get; set; } = true;

        [BsonElement("BannedAt")]
        public DateTime? BannedAt { get; set; }

        [BsonElement("Following")]
        public List<ObjectId> Following { get; set; } = new List<ObjectId>();

        [BsonElement("Followers")]
        public List<ObjectId> Followers { get; set; } = new List<ObjectId>();
    }
}