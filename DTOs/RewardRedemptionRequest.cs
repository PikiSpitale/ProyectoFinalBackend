using System.ComponentModel.DataAnnotations;
namespace ProyectAPI.DTOs;

public sealed record RewardRedemptionRequest(
    [property: Required, StringLength(80), RegularExpression("^[a-z0-9-]+$")] string ProductId,
    [property: Required, StringLength(100), RegularExpression("^[A-Za-z0-9_.:-]+$")] string IdempotencyKey);
