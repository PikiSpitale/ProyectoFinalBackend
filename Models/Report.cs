using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ProyectoFinalBackend.Models
{
    public class Report
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; set; }

        [BsonElement("reporterId")]
        public ObjectId ReporterId { get; set; }

        [BsonElement("targetId")]
        public ObjectId TargetId { get; set; }

        [BsonElement("messageId")]
        public ObjectId? MessageId { get; set; }

        [BsonElement("reason")]
        public string Reason { get; set; } = "Sin especificar";

        [BsonElement("status")]
        public string Status { get; set; } = "pending";

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("resolvedBy")]
        public ObjectId? ResolvedBy { get; set; }

        [BsonElement("resolvedAt")]
        public DateTime? ResolvedAt { get; set; }
    }
}