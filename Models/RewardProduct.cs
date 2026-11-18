namespace ProyectAPI.Models;

public sealed record RewardProduct(
    string Id, string Platform, string Product, string Region, string Currency,
    decimal FaceValue, long CostPoints, string CreditType, bool Available,
    string Compatibility, string SourceUrl, string? UnavailableReason = null);
