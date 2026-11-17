using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using ProyectAPI.Controllers;
using ProyectAPI.Services;
using ProyectAPI.DTOs;
using MongoDB.Bson;
using MongoDB.Driver;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;
using ProyectAPI.Repositories;

var checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
async Task Error(string code, Func<Task> action) {
    try { await action(); throw new Exception("Expected " + code); }
    catch (WalletOperationException ex) { Check(ex.Code == code, code); }
}
var uri = Environment.GetEnvironmentVariable("TEST_MONGODB_URI") ?? throw new Exception("TEST_MONGODB_URI required; integration cannot be skipped.");
var context = new MongoDbContext(Options.Create(new MongoDbSettings { ConnectionString = uri, DatabaseName = "wallet_tests_" + Guid.NewGuid().ToString("N") }));
var db = context.Database;
try
{
    var users = new UserRepository(context);
    var user = new User { Username = "new", Email = "new@example.invalid" };
    await users.CreateAsync(user, default);
    var wallet = await db.GetCollection<BsonDocument>("Wallets").Find(new BsonDocument("_id", ObjectId.Parse(user.Id))).FirstOrDefaultAsync();
    if (wallet is null || wallet["Balance"].ToInt64() != 1000) throw new Exception("New account must atomically receive wallet and 1000 welcome points");
    Check(true, "welcome wallet");
    var repo = new WalletRepository(context);
    var service = new WalletService(repo, users);
    var entries = db.GetCollection<PointTransaction>("PointTransactions");
    Task<PointTransaction> Debit(long n, string key) => repo.TransactionAsync((tx, ct) => tx.DebitAsync(user.Id, n, "entry", "tournament-1", key, ct), default);
    Check((await repo.HistoryAsync(user.Id, 1, 20, default)).Count == 1, "welcome ledger once");
    var debit = await Debit(250, "entry-1");
    Check(await service.BalanceAsync(user.Id, default) == 750, "debit 250");
    Check((await Debit(250, "entry-1")).Id == debit.Id && await repo.BalanceAsync(user.Id, default) == 750, "retry returns same debit");
    await Error("idempotency_conflict", () => Debit(251, "entry-1"));
    await Error("invalid_amount", () => Debit(0, "zero"));
    await Error("invalid_amount", () => Debit(-1, "negative"));
    await Error("balance_unavailable", () => Debit(751, "too-much"));
    Check(await entries.CountDocumentsAsync(x => x.UserId == user.Id) == 2, "rejections leave ledger unchanged");
    var refund = await repo.TransactionAsync((tx, ct) => tx.RefundAsync(user.Id, debit.Id, ct), default);
    Check(refund.Amount == 250 && refund.ReversesId == debit.Id && await repo.BalanceAsync(user.Id, default) == 1000, "refund linked to debit");
    await repo.TransactionAsync((tx, ct) => tx.RefundAsync(user.Id, debit.Id, ct), default);
    Check(await repo.BalanceAsync(user.Id, default) == 1000, "refund once");
    await Error("debit_not_found", () => repo.TransactionAsync((tx, ct) => tx.RefundAsync(user.Id, refund.Id, ct), default));
    var before = await entries.CountDocumentsAsync(x => x.UserId == user.Id);
    try {
        await repo.TransactionAsync<bool>(async (tx, ct) => { await tx.DebitAsync(user.Id, 250, "entry", "rollback", "rollback", ct); throw new InjectedFailure(); }, default);
    } catch (InjectedFailure) { }
    Check(await repo.BalanceAsync(user.Id, default) == 1000 && await entries.CountDocumentsAsync(x => x.UserId == user.Id) == before, "rollback of balance and ledger");
    var failedUser = new User { Id = ObjectId.GenerateNewId().ToString(), Username = "failed", Email = "failed@example.invalid" };
    try {
        await repo.TransactionAsync<bool>(async (tx, ct) => { await tx.ProvisionAsync(failedUser, ct); throw new InjectedFailure(); }, default);
    } catch (InjectedFailure) { }
    Check(await users.GetByIdAsync(failedUser.Id, default) is null && await repo.BalanceAsync(failedUser.Id, default) == 0, "failed provisioning leaves no partial account");
    await users.CreateAsync(failedUser, default);
    Check(await repo.BalanceAsync(failedUser.Id, default) == 1000, "provisioning can recover after rollback");
    var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i => {
        try { await Debit(750, "race-" + i); return true; }
        catch (WalletOperationException ex) when (ex.Code == "balance_unavailable") { return false; }
    }));
    Check(attempts.Count(x => x) == 1 && await repo.BalanceAsync(user.Id, default) == 250, "concurrent debits cannot overdraw");
    var key = new string('x', 500);
    var longDebit = await Debit(1, key);
    await repo.TransactionAsync((tx, ct) => tx.RefundAsync(user.Id, longDebit.Id, ct), default);
    Check(await repo.BalanceAsync(user.Id, default) == 250, "maximum key remains refundable");
    var repeated = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Debit(25, "same-race")));
    Check(repeated[0].Id == repeated[1].Id && await repo.BalanceAsync(user.Id, default) == 225, "concurrent identical operation once");
    var old = new User { Id = ObjectId.GenerateNewId().ToString(), Username = "old", Email = "old@example.invalid" };
    await db.GetCollection<User>("users").InsertOneAsync(old);
    Check(await service.BalanceAsync(old.Id, default) == 0 && (await service.HistoryAsync(old.Id, 1, 20, default)).Count == 0, "historical account zero without bonus");
    Check(await db.GetCollection<Wallet>("Wallets").CountDocumentsAsync(x => x.UserId == old.Id) == 0, "historical read has no writes");
    await Error("invalid_pagination", () => service.HistoryAsync(user.Id, 0, 20, default));
    await Error("invalid_pagination", () => service.HistoryAsync(user.Id, 1, 101, default));
    await Error("invalid_pagination", () => service.HistoryAsync(user.Id, int.MaxValue, 100, default));
    var first = await service.HistoryAsync(user.Id, 1, 2, default);
    var second = await service.HistoryAsync(user.Id, 2, 2, default);
    Check(first.Count == 2 && second.Count == 2 && !first.Select(x => x.Id).Intersect(second.Select(x => x.Id)).Any(), "stable paginated history");
    Check(await service.BalanceAsync(user.Id.ToUpperInvariant(), default) == 225, "canonical ObjectId");
    await Error("inactive_user", () => service.BalanceAsync(ObjectId.GenerateNewId().ToString(), default));
    old.IsActive = false; await users.UpdateAsync(old, default);
    await Error("inactive_user", () => service.BalanceAsync(old.Id, default));
    var controller = new PointsController(service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    Check(((ObjectResult)await controller.Balance(default)).StatusCode == 401, "missing claim denied");
    controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"));
    Check(await controller.Balance(default) is OkObjectResult, "authenticated balance action");
    Check(((ObjectResult)await controller.History(0,20,default)).StatusCode == 400, "HTTP pagination error");
    Check(Attribute.IsDefined(typeof(PointsController), typeof(AuthorizeAttribute)), "controller requires authentication");
    Check(typeof(PointsController).GetMethods().Count(m => m.GetCustomAttributes(typeof(HttpGetAttribute), false).Length > 0) == 2, "only two read endpoints");
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Jwt:Key"] = new string('t',64), ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test" }).Build();
    var auth = new AuthService(users, config);
    var registration = await auth.RegisterAsync(new RegisterRequest { Username = "local", Email = "local@example.invalid", Password = "Password123!" }, default);
    var local = await users.GetByEmailAsync("local@example.invalid", default);
    Check(local is not null && await repo.BalanceAsync(local.Id, default) == 1000, "local register creates funded wallet");
    await auth.LoginAsync(new LoginRequest { Email = "local@example.invalid", Password = "Password123!" }, default);
    Check(await entries.CountDocumentsAsync(x => x.UserId == local!.Id) == 1, "login does not repeat bonus");
    var total = await entries.Find(x => x.UserId == user.Id).ToListAsync();
    Check(total.Sum(x => x.Amount) == await repo.BalanceAsync(user.Id, default), "ledger reconciles balance");

    // Regression coverage for external signup, actor isolation and refund races.
    var raceDebit = await Debit(100, "refund-race");
    var refunds = await Task.WhenAll(Enumerable.Range(0,12).Select(_ => repo.TransactionAsync((tx,ct) => tx.RefundAsync(user.Id,raceDebit.Id,ct),default)));
    Check(refunds.Select(x=>x.Id).Distinct().Count()==1 && await repo.BalanceAsync(user.Id,default)==225,"twelve racing refunds credit once");
    var other = new User { Username="other", Email="other@example.invalid" };
    await users.CreateAsync(other,default);
    await Error("debit_not_found",()=>repo.TransactionAsync((tx,ct)=>tx.RefundAsync(other.Id,raceDebit.Id,ct),default));
    Check(await repo.BalanceAsync(other.Id,default)==1000,"other actor cannot refund debit");
    controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, other.Id)],"test"));
    var actorHistory = (OkObjectResult)await controller.History(1,100,default);
    var actorItems=(IEnumerable<PointTransaction>)actorHistory.Value!.GetType().GetProperty("items")!.GetValue(actorHistory.Value)!;
    Check(actorItems.All(x=>x.UserId==other.Id)&&actorItems.Count()==1,"HTTP history isolated to actor");
    var duplicate = new User { Username=other.Username, Email="duplicate@example.invalid" };
    try { await users.CreateAsync(duplicate,default);throw new Exception("duplicate accepted"); } catch(MongoWriteException ex) when(ex.WriteError.Category==ServerErrorCategory.DuplicateKey) { }
    Check(await users.GetByIdAsync(duplicate.Id,default)==null && await repo.BalanceAsync(duplicate.Id,default)==0 && await entries.CountDocumentsAsync(x=>x.UserId==duplicate.Id)==0,"duplicate identity leaves no account wallet ledger");
    async Task Google(string email,string subject,string name) {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email,email),new Claim(ClaimTypes.NameIdentifier,subject),new Claim(ClaimTypes.Name,name)],"Cookies"));
        var services = new ServiceCollection().AddSingleton<IAuthenticationService>(new TestGoogle(principal)).BuildServiceProvider();
        var google = new AuthController(auth,users,config) { ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext { RequestServices=services } } };
        Check(((RedirectResult)await google.GoogleCallback(default)).Url=="http://localhost:5173/dashboard","Google callback redirects");
    }
    await Google("google@example.invalid","google-sub","GoogleNew");
    var googleUser=(await users.GetByEmailAsync("google@example.invalid",default))!;
    Check(await repo.BalanceAsync(googleUser.Id,default)==1000 && await entries.CountDocumentsAsync(x=>x.UserId==googleUser.Id)==1,"Google new account funded once");
    await Google("google@example.invalid","google-sub","GoogleNew");
    Check(await entries.CountDocumentsAsync(x=>x.UserId==googleUser.Id)==1,"Google repeat login no bonus");
    await Google("local@example.invalid","linked-sub","Local");
    Check((await users.GetByIdAsync(local!.Id,default))!.ExternalId=="linked-sub" && await entries.CountDocumentsAsync(x=>x.UserId==local.Id)==1,"Google local linking preserves wallet");
    var historical = new User { Id=ObjectId.GenerateNewId().ToString(),Username="historicalGoogle",Email="historicalGoogle@example.invalid".ToLowerInvariant() };
    await db.GetCollection<User>("users").InsertOneAsync(historical);
    await Google(historical.Email,"historical-sub","Historical");
    Check(await repo.BalanceAsync(historical.Id,default)==0 && await entries.CountDocumentsAsync(x=>x.UserId==historical.Id)==0,"historical Google linking no retroactive bonus");
    Check((await entries.Find(x=>x.UserId==user.Id).ToListAsync()).Sum(x=>x.Amount)==await repo.BalanceAsync(user.Id,default),"ledger reconciles after refund race");
    Console.WriteLine($"{checks} wallet checks passed against disposable MongoDB.");
}
finally { await db.Client.DropDatabaseAsync(db.DatabaseNamespace.DatabaseName); }

sealed class InjectedFailure : Exception { }

sealed class TestGoogle(ClaimsPrincipal principal) : IAuthenticationService {
 public Task<AuthenticateResult> AuthenticateAsync(HttpContext c,string? s)=>Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal,"Cookies")));
 public Task ChallengeAsync(HttpContext c,string? s,AuthenticationProperties? p)=>Task.CompletedTask;
 public Task ForbidAsync(HttpContext c,string? s,AuthenticationProperties? p)=>Task.CompletedTask;
 public Task SignInAsync(HttpContext c,string? s,ClaimsPrincipal u,AuthenticationProperties? p)=>Task.CompletedTask;
 public Task SignOutAsync(HttpContext c,string? s,AuthenticationProperties? p)=>Task.CompletedTask;
}
