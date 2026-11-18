using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using ProyectAPI.Controllers;
using ProyectAPI.DTOs;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;
using ProyectAPI.Repositories;
using ProyectAPI.Services;
var checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
async Task Error(string code, Func<Task> action) { try { await action(); } catch (WalletOperationException e) { Check(e.Code == code, code); return; } throw new Exception("Expected " + code); }
var catalog = new RewardCatalog();
Check(catalog.Products().Any(x => x.Id == "steam-usd-10"), "verified Steam USD10 catalogue");
Check(catalog.Products().Select(x => x.Platform).Distinct().Count() == 7 && catalog.Products().Count(x => x.Platform == "Riot") == 2, "all required platforms and separate Riot products");
Check(catalog.Products().All(x => x.Currency == "USD" && x.Region == "US" && x.CostPoints == x.FaceValue * 2000), "real USD face values and server prices");
Check(catalog.Find("epic-usd-15")!.CreditType == "PlatformBalance", "Epic account balance product");
var uri = Environment.GetEnvironmentVariable("TEST_MONGODB_URI") ?? throw new Exception("TEST_MONGODB_URI required; no skipped integration.");
var context = new MongoDbContext(Options.Create(new MongoDbSettings { ConnectionString = uri, DatabaseName = "reward_tests_" + Guid.NewGuid().ToString("N") }));
var db = context.Database; var wallets = new WalletRepository(context); var repo = new RewardRepository(context); var service = new RewardService(repo, wallets, catalog, new PreciseClock()); var users = new UserRepository(context);
async Task<User> User(long points) { var u = new User { Username = Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid" }; await users.CreateAsync(u, default); if (points > 1000) await wallets.TransactionAsync(async (w, ct) => { await w.CreditAsync(u.Id, points - 1000, "fixture", "fixture", "fixture", ct); return true; }, default); return u; }
RewardRedemptionRequest Request(string key, string product = "steam-usd-10") => new(product, key);
async Task<long> Balance(User u) => await wallets.BalanceAsync(u.Id, default);
async Task<long> Count(User u) => await db.GetCollection<RewardRedemption>("RewardRedemptions").CountDocumentsAsync(x => x.UserId == u.Id);
try
{
    foreach (var product in catalog.Products())
    {
        var buyer = await User(product.CostPoints + 1000); var delivered = await service.Redeem(buyer.Id, Request("all-products", product.Id), default);
        Check(delivered.Product == product && delivered.Code.StartsWith("DEMO-") && await Balance(buyer) == 1000, "real catalogue product snapshot and debit " + product.Id);
    }
    var user = await User(22500);
    var receipt = await service.Redeem(user.Id, Request("example"), default);
    Check(receipt.CostPoints == 20000 && await Balance(user) == 2500, "22500 minus Steam10 gives2500");
    Check(receipt.IsSimulation && receipt.Code.StartsWith("DEMO-STEAM-") && receipt.Disclaimer.Contains("Not valid"), "explicit fictitious code and disclaimer");
    var replay = await service.Redeem(user.Id, Request("example"), default);
    Check(replay.Id == receipt.Id && replay.Code == receipt.Code && await Balance(user) == 2500 && await Count(user) == 1, "retry returns original receipt without debit");
    await Error("idempotency_conflict", () => service.Redeem(user.Id, Request("example", "xbox-usd-10"), default));
    var retiredService = new RewardService(repo, wallets, new RewardCatalog(Array.Empty<RewardProduct>()), TimeProvider.System);
    Check((await retiredService.Redeem(user.Id, Request("example"), default)).Code == receipt.Code, "retry after catalog removal returns original");
    var changedCatalog = new RewardCatalog(new[] { catalog.Find("steam-usd-10")! with { Product = "Changed", CostPoints = 1, Available = false } });
    var changedService = new RewardService(repo, wallets, changedCatalog, TimeProvider.System);
    Check((await changedService.Redeem(user.Id, Request("example"), default)).Product.Product == "Steam Wallet gift card", "receipt snapshot survives price/product edits");
    await Error("product_unavailable", () => changedService.Redeem(user.Id, Request("new-unavailable"), default));
    await Error("product_not_found", () => service.Redeem(user.Id, Request("missing", "missing"), default));
    await Error("balance_unavailable", () => service.Redeem(user.Id, Request("broke"), default));
    Check(await Balance(user) == 2500 && await Count(user) == 1, "failed requests preserve wallet and receipts");
    await Error("invalid_redemption", () => service.Redeem(user.Id, Request(""), default));
    await Error("invalid_redemption", () => service.Redeem(user.Id, Request(new string('x', 101)), default));
    await Error("invalid_redemption", () => service.Redeem(user.Id, Request("bad\nkey"), default));
    await Error("invalid_redemption", () => service.Redeem(user.Id, new RewardRedemptionRequest(null!, "key"), default));
    foreach (var (page, size) in new[] { (0, 20), (1, 0), (1, 101), (int.MaxValue, 100) }) await Error("invalid_pagination", () => service.History(user.Id, page, size, default));
    var other = await User(22500);
    Check((await service.History(other.Id, 1, 20, default)).Count == 0, "history isolates users");
    var otherReceipt = await service.Redeem(other.Id, Request("example"), default);
    Check(otherReceipt.Id != receipt.Id && otherReceipt.Code != receipt.Code, "same idempotency key is scoped by user and codes unique");
    await db.GetCollection<User>("users").UpdateOneAsync(x => x.Id == other.Id, Builders<User>.Update.Set(x => x.IsActive, false));
    await Error("inactive_user", () => service.Redeem(other.Id, Request("example"), default));
    await Error("inactive_user", () => service.History(other.Id, 1, 20, default));
    // Concurrent retries of one event charge once and return exactly one code.
    var race = await User(22500); var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.Redeem(race.Id, Request("race"), default)));
    Check(results.Select(x => x.Id).Distinct().Count() == 1 && results.Select(x => x.Code).Distinct().Count() == 1 && await Balance(race) == 2500 && await Count(race) == 1, "concurrent retries debit once");
    // Different products with one key elect one payload; the other conflicts.
    var mixed = await User(50000);
    async Task<string> Mixed(string product) { try { return (await service.Redeem(mixed.Id, Request("mixed", product), default)).Product.Id; } catch (WalletOperationException e) { return e.Code; } }
    var mixedResults = await Task.WhenAll(Mixed("steam-usd-10"), Mixed("xbox-usd-10"));
    Check(mixedResults.Count(x => x == "idempotency_conflict") == 1 && await Balance(mixed) == 30000 && await Count(mixed) == 1, "competing payloads same key charge one");
    // Two distinct redemptions compete for the final funds.
    var spent = await User(22500);
    async Task<bool> Spend(string key) { try { await service.Redeem(spent.Id, Request(key), default); return true; } catch (WalletOperationException e) when (e.Code == "balance_unavailable") { return false; } }
    var spending = await Task.WhenAll(Spend("one"), Spend("two"));
    Check(spending.Count(x => x) == 1 && await Balance(spent) == 2500 && await Count(spent) == 1, "last funds race never overspends");
    // Fail receipt insertion after wallet debit; transaction must roll back everything.
    var rollback = await User(50000);
    await db.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", "RewardRedemptions" }, { "validator", new BsonDocument("CostPoints", new BsonDocument("$lt", 0)) } });
    try { await service.Redeem(rollback.Id, Request("rollback"), default); throw new Exception("Expected receipt validation failure"); } catch (MongoWriteException) { Check(await Balance(rollback) == 50000 && await Count(rollback) == 0, "receipt insertion failure rolls back debit"); }
    await db.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", "RewardRedemptions" }, { "validator", new BsonDocument() } });
    await service.Redeem(rollback.Id, Request("rollback"), default);
    Check(await Balance(rollback) == 30000 && await Count(rollback) == 1, "retry after rollback succeeds once");
    var controller = new RewardsController(service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) }, "test")) } } };
    Check(await controller.Redeem(Request("example"), default) is OkObjectResult, "controller derives actor from authenticated claims");
    var anonymous = new RewardsController(service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    Check(await anonymous.Redeem(Request("example"), default) is ObjectResult { StatusCode: 401 }, "missing claims returns401");
    foreach (var name in new[] { "Redeem", "History" }) Check(typeof(RewardsController).GetMethod(name)!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Length == 1, "authenticated route " + name);
    Check(controller.Catalog() is OkObjectResult, "catalog accessible without spending");
    var history = await service.History(user.Id, 1, 1, default); Check(history.Count == 1 && history[0].Id == receipt.Id && (await service.History(user.Id, 2, 1, default)).Count == 0, "history stable pagination");
    var indexes = await db.GetCollection<BsonDocument>("RewardRedemptions").Indexes.List().ToListAsync();
    Check(indexes.Count(x => x.Contains("unique") && x["unique"].AsBoolean) == 2, "unique user key and demo code indexes");
    foreach (var u in new[] { user, race, mixed, spent, rollback })
    {
        var entries = await db.GetCollection<PointTransaction>("PointTransactions").Find(x => x.UserId == u.Id).ToListAsync(); Check(entries.Sum(x => x.Amount) == await Balance(u), "wallet reconciles with ledger");
        var receipts = await service.History(u.Id, 1, 100, default); Check(receipts.All(r => entries.Any(e => e.Id == r.DebitId && e.Amount == -r.CostPoints)), "every receipt has matching debit");
    }
    var initialOutput = System.Text.Json.JsonSerializer.Serialize(receipt);
    var replayOutput = System.Text.Json.JsonSerializer.Serialize(replay);
    async Task<string> StateHash()
    {
        var text = "";
        foreach (var name in new[] { "Wallets", "PointTransactions", "RewardRedemptions" })
        {
            var docs = await db.GetCollection<BsonDocument>(name).Find(new BsonDocument()).Sort(new BsonDocument("_id", 1)).ToListAsync();
            text += name + string.Join("", docs.Select(x => x.ToJson()));
        }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }
    var before = await StateHash();
    await Error("idempotency_conflict", () => service.Redeem(user.Id, Request("example", "xbox-usd-10"), default));
    Check(before == await StateHash(), "independent rejected payload preserves persisted state hash");
    var crossDomain = await User(22500);
    async Task<bool> ExistingDebit() { try { await wallets.TransactionAsync(async (w, ct) => { await w.DebitAsync(crossDomain.Id, 5000, "fixture_existing_flow", "existing", "existing", ct); return true; }, default); return true; } catch (WalletOperationException e) when (e.Code == "balance_unavailable") { return false; } }
    async Task<bool> NewRedemption() { try { await service.Redeem(crossDomain.Id, Request("cross-domain"), default); return true; } catch (WalletOperationException e) when (e.Code == "balance_unavailable") { return false; } }
    var crossResults = await Task.WhenAll(ExistingDebit(), NewRedemption());
    Check(crossResults.Count(x => x) == 1 && await Balance(crossDomain) >= 0, "independent reward and preexisting debit share spending fence");
    var crossEntries = await db.GetCollection<PointTransaction>("PointTransactions").Find(x => x.UserId == crossDomain.Id).ToListAsync();
    Check(crossEntries.Sum(x => x.Amount) == await Balance(crossDomain), "independent cross-domain ledger reconciles");
    Check(initialOutput == replayOutput, "whole serialized receipt is identical on replay");
    Check(results.Select(x => System.Text.Json.JsonSerializer.Serialize(x)).Distinct().Count() == 1, "concurrent retries return identical whole receipts");
    Console.WriteLine($"{checks} reward checks passed against disposable MongoDB");
}
finally { await db.Client.DropDatabaseAsync(db.DatabaseNamespace.DatabaseName); }

sealed class PreciseClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-11-18T12:00:00.1234567Z");
}
