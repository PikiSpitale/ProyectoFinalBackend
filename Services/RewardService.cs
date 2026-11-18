using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ProyectAPI.DTOs;
using ProyectAPI.Models;
using ProyectAPI.Repositories;
namespace ProyectAPI.Services;

public sealed class RewardService(RewardRepository repository, WalletRepository wallets, RewardCatalog catalog, TimeProvider clock)
{
    private static WalletOperationException Error(int status, string code, string message) => new(status, code, message);
    public IReadOnlyList<RewardProduct> Catalog() => catalog.Products();
    public async Task<RewardRedemption> Redeem(string userId, RewardRedemptionRequest request, CancellationToken ct)
    {
        var user = WalletRepository.UserId(userId);
        if (request == null || string.IsNullOrEmpty(request.ProductId) || request.ProductId.Length > 80 || !Regex.IsMatch(request.ProductId, "\\A[a-z0-9-]+\\z") || string.IsNullOrEmpty(request.IdempotencyKey) || request.IdempotencyKey.Length > 100 || !Regex.IsMatch(request.IdempotencyKey, "\\A[A-Za-z0-9_.:-]+\\z"))
            throw Error(400, "invalid_redemption", "Product and idempotency key are required and must have a valid format.");
        var receiptId = user + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.IdempotencyKey)));
        await repository.Initialize(ct);
        return await wallets.TransactionAsync(async (wallet, token) =>
        {
            if (!await repository.Active(user, wallet.Session, token)) throw Error(401, "inactive_user", "The account is not active.");
            var previous = await repository.Find(receiptId, wallet.Session, token);
            if (previous != null)
            {
                if (previous.Product.Id != request.ProductId) throw Error(409, "idempotency_conflict", "This key already identifies another product.");
                return previous;
            }
            var product = catalog.Find(request.ProductId) ?? throw Error(404, "product_not_found", "Reward product not found.");
            if (!product.Available) throw Error(409, "product_unavailable", product.UnavailableReason ?? "Reward product unavailable.");
            if (product.CostPoints <= 0) throw Error(409, "product_unavailable", "Reward product has no valid price.");
            var debit = await wallet.DebitAsync(user, product.CostPoints, "reward_redemption", receiptId, "reward:" + request.IdempotencyKey, token);
            var receipt = new RewardRedemption { Id = receiptId, UserId = user, IdempotencyKey = request.IdempotencyKey, Product = product with { }, CostPoints = product.CostPoints, DebitId = debit.Id, Code = "DEMO-" + product.Platform.ToUpperInvariant() + "-" + Guid.NewGuid().ToString("N").ToUpperInvariant(), CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds()).UtcDateTime };
            await repository.Insert(receipt, wallet.Session, token);
            return receipt;
        }, ct);
    }
    public async Task<List<RewardRedemption>> History(string userId, int page, int pageSize, CancellationToken ct)
    {
        var user = WalletRepository.UserId(userId);
        if (page < 1 || pageSize < 1 || pageSize > 100 || (long)(page - 1) * pageSize > int.MaxValue) throw Error(400, "invalid_pagination", "Page must be positive; page size must be from 1 to 100.");
        if (!await repository.Active(user, ct)) throw Error(401, "inactive_user", "The account is not active.");
        return await repository.History(user, page, pageSize, ct);
    }
}
