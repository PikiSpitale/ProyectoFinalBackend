using System.ComponentModel.DataAnnotations;
namespace ProyectAPI.DTOs;

public sealed record RewardRedemptionRequest(
    [Required, StringLength(80), RegularExpression("^[a-z0-9-]+$")] string ProductId,
    [Required, StringLength(100), RegularExpression("^[A-Za-z0-9_.:-]+$")] string IdempotencyKey);
