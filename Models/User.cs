using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ProyectAPI.Models;

[BsonIgnoreExtraElements]
public sealed class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = "User";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;

    // Campo para guardar la URL de la foto de perfil del usuario
    public string AvatarUrl { get; set; } = string.Empty;

    // Lista de juegos asociados al usuario
    public List<UserGame> Games { get; set; } = new();

    // Estadísticas globales o competitivas
    public UserStats Stats { get; set; } = new();
}