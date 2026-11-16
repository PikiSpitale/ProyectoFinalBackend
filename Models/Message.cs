using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ProyectoFinalBackend.Models
{
    public class Message
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; set; }

        [BsonElement("senderId")]
        public ObjectId SenderId { get; set; }

        [BsonElement("receiverId")]
        public ObjectId ReceiverId { get; set; }

        [BsonElement("content")]
        public string Content { get; set; }

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("edited")]
        public bool Edited { get; set; } = false;

        [BsonElement("editedAt")]
        public DateTime? EditedAt { get; set; }

        [BsonElement("deleted")]
        public bool Deleted { get; set; } = false;

        [BsonElement("deliveryStatus")]
        public string DeliveryStatus { get; set; } = "direct";

        [BsonElement("read")]
        public bool Read { get; set; } = false;
    }
}