using System.ComponentModel.DataAnnotations;

namespace ProyectAPI.DTOs;

public sealed class InviteTeamDto
{
    [Required, RegularExpression("^[a-fA-F0-9]{24}$")]
    public string RecipientId { get; set; } = "";
}

public sealed record InviteeDto(string Id, string Username, string AvatarUrl);
public sealed record TeamInvitationDto(string Id, string TeamId, string TeamName,
    string SenderId, string SenderUsername, string RecipientId, string RecipientUsername,
    string Status, DateTime CreatedAt, DateTime ExpiresAt);
