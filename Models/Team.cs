using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ProyectAPI.Models
{
    public class Team
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Tag { get; set; } = string.Empty; 

        public string LogoUrl { get; set; } = string.Empty;

        public string CaptainId { get; set; } = string.Empty; 

        public List<TeamMember> Members { get; set; } = new();
    }

    
}