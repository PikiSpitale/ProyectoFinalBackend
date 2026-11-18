using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using ProyectAPI.Controllers;
using ProyectAPI.DTOs;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;
using ProyectAPI.Repositories;
using ProyectAPI.Services;
var checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
async Task Error(string code, Func<Task> action) { try { await action(); } catch (WalletOperationException e) { Check(e.Code == code, code); return; } throw new Exception("Expected " + code); }
Check(EloCalculator.Next(1000, 1000, true) == 1016 && EloCalculator.Next(1000, 1000, false) == 984, "equal1000 becomes1016/984");
Check(EloCalculator.Next(1000, 1400, true) - 1000 > EloCalculator.Next(1000, 600, true) - 1000, "upset earns more against stronger rival");
Check(EloCalculator.Next(0, 0, false) == -16, "integer rounding does not clamp ratings");
Check(EloCalculator.Game("  vALorant ") == "Valorant", "canonical game trim and case");
Check(EloCalculator.Next(0, 400 * Math.Log10(1 / .484375 - 1), false) == -16, "midpoint rounds away from zero");
var uri = Environment.GetEnvironmentVariable("TEST_MONGODB_URI") ?? throw new Exception("TEST_MONGODB_URI required");
var context = new MongoDbContext(Options.Create(new MongoDbSettings { ConnectionString = uri, DatabaseName = "elo_tests_" + Guid.NewGuid().ToString("N") }));
var db = context.Database; var wallet = new WalletRepository(context); var repository = new EloRepository(context); var clock = TimeProvider.System; var elo = new EloService(repository, clock); var economy = new TournamentEconomyService(new TournamentEconomyRepository(context), wallet, clock, elo); var rankings = new RankingService(repository); var users = new UserRepository(context);
async Task<User> User() { var u = new User { Username = Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid", Stats = new UserStats { Points = 999999 } }; await users.CreateAsync(u, default); return u; }
async Task<List<TournamentEnrollment>> Entrants(bool team = false) { var result = new List<TournamentEnrollment>(); for (var i = 0; i < 8; i++) { var e = new TournamentEnrollment { Status = "Admitted", TeamId = team ? ObjectId.GenerateNewId().ToString() : null, TeamName = team ? "Snapshot team " + i : null }; for (var j = 0; j < (team ? 5 : 1); j++) { var u = await User(); e.Players.Add(new TournamentPayer { UserId = u.Id, Username = u.Username, InGameName = "Fixture", Rank = "Oro", Confirmed = true }); } result.Add(e); } return result; }
async Task<(Tournament, List<TournamentMatch>)> Finished(List<TournamentEnrollment> entrants, string game, bool free = false) { var t = new Tournament { Id = ObjectId.GenerateNewId().ToString(), GameName = game, Title = "fixture", EconomyVersion = 1, Status = TournamentStatuses.Finished, StartDate = DateTime.UtcNow.AddHours(-2), EndDate = DateTime.UtcNow.AddHours(-1), IsFree = free, TournamentType = entrants[0].TeamId == null ? "Individual" : "Equipo", Enrollments = entrants.Select(x => new TournamentEnrollment { Id = Guid.NewGuid().ToString("N"), Status = "Admitted", TeamId = x.TeamId, TeamName = x.TeamName, Players = x.Players.Select(p => new TournamentPayer { UserId = p.UserId, Username = p.Username, InGameName = p.InGameName, Rank = p.Rank, Confirmed = true }).ToList() }).ToList(), Participants = entrants.Select(x => new TournamentParticipant { UserId = x.Players[0].UserId, Username = x.Players[0].Username, TeamId = x.TeamId, TeamName = x.TeamName }).ToList() }; var matches = BracketGenerator.Generate(t, new Random(19)); foreach (var m in matches.OrderBy(x => x.Round).ThenBy(x => x.Position)) { m.Id = ObjectId.GenerateNewId().ToString(); m.WinnerUserId = m.PlayerA!.UserId; m.ScoreA = 13; m.ScoreB = 0; m.Status = MatchStatuses.Finished; var next = matches.SingleOrDefault(x => x.Round == m.Round + 1 && x.Position == m.Position / 2); if (next != null) { if (m.Position % 2 == 0) next.PlayerA = m.PlayerA; else next.PlayerB = m.PlayerA; } } await db.GetCollection<Tournament>("Tournaments").InsertOneAsync(t); await db.GetCollection<TournamentMatch>("Matches").InsertManyAsync(matches); return (t, matches); }
async Task<long> HistoryCount(string id) => await db.GetCollection<EloAdjustment>("EloAdjustments").CountDocumentsAsync(x => x.TournamentId == id);
async Task<List<EloAdjustment>> History(string entity, string game = "Valorant", string kind = "player") => await rankings.History(kind, entity, game, 1, 100, default);
async Task<EloRating?> Rating(string entity, string game = "Valorant", string kind = "player") => await db.GetCollection<EloRating>(kind == "player" ? "PlayerEloRatings" : "TeamEloRatings").Find(x => x.EntityId == entity && x.GameName == game).FirstOrDefaultAsync();
try
{
    var entrants = await Entrants(); var (first, matches) = await Finished(entrants, "Valorant", true);
    await economy.Close(first.Id, "moderator", default);
    Check(await HistoryCount(first.Id) == 14, "free tournament processes seven played matches");
    var firstMatch = matches.First(x => x.Round == 1 && x.Position == 0); var firstWinner = (await History(firstMatch.PlayerA!.UserId)).Single(x => x.MatchId == firstMatch.Id);
    Check(firstWinner.Before == 1000 && firstWinner.After == 1016 && firstWinner.OpponentRating == 1000 && firstWinner.RuleVersion == 1, "initial match history records1016 and expectation");
    var firstLoser = (await History(firstMatch.PlayerB!.UserId)).Single(x => x.MatchId == firstMatch.Id); Check(firstLoser.After == 984, "loser uses prematch winner1000");
    var champion = matches.Single(x => x.Round == 3).WinnerUserId!;
    var championHistory = (await History(champion)).OrderBy(x => x.Round).ToList(); Check(championHistory.Count == 3 && championHistory[1].Before == championHistory[0].After && championHistory[2].Before == championHistory[1].After, "round ordered adjustments see previous round");
    var balance = await wallet.BalanceAsync(champion, default); await Task.WhenAll(economy.Close(first.Id, "moderator", default), economy.Close(first.Id, "moderator", default));
    Check(await HistoryCount(first.Id) == 14 && await wallet.BalanceAsync(champion, default) == balance, "closure replay duplicates neither Elo nor prize");
    Check((await db.GetCollection<Tournament>("Tournaments").Find(x => x.Id == first.Id).SingleAsync()).EloRuleVersion == 1, "tournament persists Elo rule marker");
    async Task<string> PersistedHash()
    {
        var snapshot = "";
        foreach (var collection in new[] { "Tournaments", "Matches", "Wallets", "PointTransactions", "PlayerEloRatings", "TeamEloRatings", "EloAdjustments", "EloSettlements", "EloGames" })
            snapshot += (await db.GetCollection<BsonDocument>(collection).Find(new BsonDocument()).Sort(new BsonDocument("_id", 1)).ToListAsync()).ToJson();
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(snapshot)));
    }
    var stateBeforeRejectedEdit = await PersistedHash();
    var tournamentService = new TournamentService(new TournamentRepository(context), new MatchRepository(context), new TeamRepository(context), users, economy);
    try
    {
        await tournamentService.SetMatchResultAsync(first.Id, firstMatch.Id, new MatchResultRequest(0, 13, firstMatch.PlayerB.UserId), default);
        throw new Exception("Expected closed-result edit rejection");
    }
    catch (InvalidOperationException) { }
    Check(stateBeforeRejectedEdit == await PersistedHash(), "rejected closed result preserves all rating prize and history state");
    var old = await Finished(entrants, "Valorant"); await db.GetCollection<Tournament>("Tournaments").UpdateOneAsync(x => x.Id == old.Item1.Id, Builders<Tournament>.Update.Set(x => x.ClosedAt, DateTime.UtcNow));
    await economy.Close(old.Item1.Id, "moderator", default); Check(await HistoryCount(old.Item1.Id) == 0, "previously closed tournament receives no historical Elo");
    var otherGame = await Finished(entrants, "  rocket LEAGUE  "); await economy.Close(otherGame.Item1.Id, "moderator", default);
    Check((await Rating(firstMatch.PlayerB.UserId, "Rocket League"))!.Rating == 984, "new game starts independently at1000");
    Check((await rankings.Rankings("player", "Rocket League", 1, 100, default)).Count == 8 && (await rankings.Rankings("player", "Valorant", 1, 100, default)).All(x => x.GameName == "Valorant"), "rankings filter canonical game");
    Check((await users.GetByIdAsync(champion, default))!.Stats.Points == 999999, "legacy Stats.Points unchanged and not Elo seed");
    var legacy = (await rankings.GetLeaderboardAsync("Valorant", default)).ToList(); Check(legacy.Count == 8 && legacy.All(x => x.Points == x.Rating && x.GameName == "Valorant"), "legacy Points field is explicit Elo alias");
    // Different closures race on a previously nonexistent game gate, sharing all players.
    var concurrentA = await Finished(entrants, "CS:GO"); var concurrentB = await Finished(entrants, "cs:go");
    var independentRepository = new EloRepository(context);
    var independentEconomy = new TournamentEconomyService(new TournamentEconomyRepository(context), new WalletRepository(context), clock, new EloService(independentRepository, clock));
    await Task.WhenAll(economy.Close(concurrentA.Item1.Id, "moderator", default), independentEconomy.Close(concurrentB.Item1.Id, "moderator", default));
    var expected = entrants.ToDictionary(x => x.Players[0].UserId, x => 1000);
    foreach (var ignored in new[] { 0, 1 }) foreach (var m in concurrentA.Item2.OrderBy(x => x.Round).ThenBy(x => x.Position)) { var a = expected[m.PlayerA!.UserId]; var b = expected[m.PlayerB!.UserId]; expected[m.PlayerA.UserId] = EloCalculator.Next(a, b, true); expected[m.PlayerB.UserId] = EloCalculator.Next(b, a, false); }
    foreach (var pair in expected) Check((await Rating(pair.Key, "CS:GO"))!.Rating == pair.Value, "concurrent closures preserve serial Elo " + pair.Key);
    Check(await HistoryCount(concurrentA.Item1.Id) == 14 && await HistoryCount(concurrentB.Item1.Id) == 14, "concurrent first game closes retain both histories");
    // Team rating starts independently from personal averages and snapshots survive source edits.
    var teams = await Entrants(true); var teamEvent = await Finished(teams, "League of Legends"); var tm = teamEvent.Item2.First(x => x.Round == 1 && x.Position == 0); var left = teamEvent.Item1.Enrollments.Single(x => x.Players[0].UserId == tm.PlayerA!.UserId); var right = teamEvent.Item1.Enrollments.Single(x => x.Players[0].UserId == tm.PlayerB!.UserId);
    await repository.Prepare("League of Legends", default);
    await wallet.TransactionAsync(async (w, ct) => { for (var i = 0; i < 5; i++) { var a = await repository.Rating("player", left.Players[i].UserId, left.Players[i].Username, "League of Legends", w.Session, ct); a.Rating = 800 + i * 100; await repository.Save(a, w.Session, ct); var b = await repository.Rating("player", right.Players[i].UserId, right.Players[i].Username, "League of Legends", w.Session, ct); b.Rating = 1200 + i * 100; await repository.Save(b, w.Session, ct); } var team = await repository.Rating("team", left.TeamId!, left.TeamName!, "League of Legends", w.Session, ct); team.Rating = 1600; await repository.Save(team, w.Session, ct); return true; }, default);
    var sourceTeam = new Team { Id = left.TeamId!, Name = "Edited source", GameName = "League of Legends", CaptainId = (await User()).Id, Members = new() }; await db.GetCollection<Team>("Teams").InsertOneAsync(sourceTeam);
    await economy.Close(teamEvent.Item1.Id, "moderator", default);
    var personalLeft = (await History(left.Players[0].UserId, "League of Legends")).Single(x => x.MatchId == tm.Id); var personalRight = (await History(right.Players[0].UserId, "League of Legends")).Single(x => x.MatchId == tm.Id);
    Check(personalLeft.OpponentRating == 1400 && personalLeft.After == EloCalculator.Next(800, 1400, true), "personal team delta uses five rivals average");
    Check(personalRight.OpponentRating == 1000 && personalRight.After == EloCalculator.Next(1200, 1000, false), "both sides use prematch snapshots");
    var teamDelta = (await History(left.TeamId!, "League of Legends", "team")).Single(x => x.MatchId == tm.Id);
    Check(teamDelta.Before == 1600 && teamDelta.OpponentRating == 1000 && teamDelta.After == EloCalculator.Next(1600, 1000, true), "team Elo is own rating not roster mean");
    Check(await HistoryCount(teamEvent.Item1.Id) == 84, "team match history includes two teams and ten players");
    Check((await History(sourceTeam.CaptainId, "League of Legends")).Count == 0 && (await History(left.Players[0].UserId, "League of Legends")).Count > 0, "source roster edits do not change fixed eligible players");
    Check((await Rating(left.TeamId!, "League of Legends", "team"))!.Name == left.TeamName, "team name from tournament snapshot survives edits");
    // The same team identifier carries its rating across a new fixed roster.
    var retainedTeamRating = (await Rating(left.TeamId!, "League of Legends", "team"))!.Rating;
    var changedTeams = await Entrants(true); changedTeams[0].TeamId = left.TeamId; changedTeams[0].TeamName = left.TeamName;
    var changedEvent = await Finished(changedTeams, "League of Legends"); await economy.Close(changedEvent.Item1.Id, "moderator", default);
    var changedFirst = (await History(left.TeamId!, "League of Legends", "team")).Where(x => x.TournamentId == changedEvent.Item1.Id).OrderBy(x => x.Round).First();
    Check(changedFirst.Before == retainedTeamRating, "team rating persists across later roster changes");
    // Eight-slot closure forbids byes; the Elo processor also explicitly ignores them.
    var bye = await Finished(await Entrants(), "Valorant", true); foreach (var m in bye.Item2) m.IsBye = true;
    await elo.Prepare(bye.Item1.Id, default); await wallet.TransactionAsync(async (w, ct) => { await elo.Process(bye.Item1, bye.Item2, w.Session, ct); return true; }, default);
    Check(await HistoryCount(bye.Item1.Id) == 0, "bye matches produce no Elo adjustments");
    // Walkover awards no Elo to either side for that match, even though bracket advances.
    var walk = await Finished(await Entrants(), "Valorant", true); var wo = walk.Item2.First(x => x.Round == 1 && x.Position == 0); await db.GetCollection<TournamentMatch>("Matches").UpdateOneAsync(x => x.Id == wo.Id, Builders<TournamentMatch>.Update.Set(x => x.IsWalkover, true)); await economy.Close(walk.Item1.Id, "moderator", default);
    Check(await HistoryCount(walk.Item1.Id) == 12 && (await History(wo.PlayerB!.UserId)).Count == 0, "walkover creates no adjustment for either side");
    // Elo is inside the same closure transaction as rewards: force history failure.
    var failed = await Finished(await Entrants(), "Valorant"); var beforeWallets = await db.GetCollection<BsonDocument>("Wallets").Find(new BsonDocument()).Sort(new BsonDocument("_id", 1)).ToListAsync();
    await db.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", "EloAdjustments" }, { "validator", new BsonDocument("RuleVersion", new BsonDocument("$lt", 0)) } });
    try { await economy.Close(failed.Item1.Id, "moderator", default); throw new Exception("Expected forced history insertion failure"); } catch (MongoWriteException) { Check(await HistoryCount(failed.Item1.Id) == 0 && (await db.GetCollection<Tournament>("Tournaments").Find(x => x.Id == failed.Item1.Id).SingleAsync()).ClosedAt == null, "failed history rolls back closure and adjustments"); }
    Check(await db.GetCollection<EloRating>("PlayerEloRatings").CountDocumentsAsync(Builders<EloRating>.Filter.In(x => x.EntityId, failed.Item1.Enrollments.SelectMany(e => e.Players.Select(p => p.UserId)))) == 0, "failed Elo insertion rolls back newly created ratings");
    Check(await db.GetCollection<BsonDocument>("EloSettlements").CountDocumentsAsync(new BsonDocument("_id", failed.Item1.Id)) == 0, "failed Elo insertion leaves no processing marker");
    var afterWallets = await db.GetCollection<BsonDocument>("Wallets").Find(new BsonDocument()).Sort(new BsonDocument("_id", 1)).ToListAsync(); Check(beforeWallets.ToJson() == afterWallets.ToJson(), "failed Elo processing rolls back all prize credits");
    await db.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", "EloAdjustments" }, { "validator", new BsonDocument() } }); await economy.Close(failed.Item1.Id, "moderator", default); Check(await HistoryCount(failed.Item1.Id) == 14, "failure retry processes once");
    await Error("invalid_game", () => rankings.Rankings("player", "unknown", 1, 20, default)); await Error("invalid_game", () => rankings.Rankings("team", null, 1, 20, default)); await Error("invalid_user", () => rankings.History("player", "bad", "Valorant", 1, 20, default));
    foreach (var (page, size) in new[] { (0, 20), (1, 101), (int.MaxValue, 100) }) await Error("invalid_pagination", () => rankings.Rankings("player", "Valorant", page, size, default));
    var all = (await rankings.Rankings("player", "Valorant", 1, 100, default)); var firstPage = await rankings.Rankings("player", "Valorant", 1, 1, default); var secondPage = await rankings.Rankings("player", "Valorant", 2, 1, default); Check(firstPage[0] == all[0] && secondPage[0] == all[1], "pagination follows deterministic ranking order");
    // Explicit ties order by normalized name then immutable entity ID.
    var tieIds = new[] { ObjectId.GenerateNewId().ToString(), ObjectId.GenerateNewId().ToString() };
    await wallet.TransactionAsync(async (w, ct) => { foreach (var id in tieIds.Reverse()) { var value = await repository.Rating("team", id, "Same name", "CS:GO", w.Session, ct); await repository.Save(value, w.Session, ct); } return true; }, default);
    var tied = await rankings.Rankings("team", "CS:GO", 1, 20, default); Check(tied.Select(x => x.EntityId).SequenceEqual(tieIds.OrderBy(x => x, StringComparer.Ordinal)), "equal rating and name order by stable ID");
    var controller = new RankingsController(rankings); Check(await controller.Players("unknown") is ObjectResult { StatusCode: 400 }, "invalid game returns400 from public controller"); Check(await controller.Teams("League of Legends") is OkObjectResult, "public team ranking route");
    // Independent probe: shared team and player identities across concurrent closures.
    var raceTeamA=await Finished(teams,"Valorant");var raceTeamB=await Finished(teams,"Valorant");
    var serialPlayers=teams.SelectMany(x=>x.Players).ToDictionary(x=>x.UserId,x=>1000);
    var serialTeams=teams.ToDictionary(x=>x.TeamId!,x=>1000);
    foreach(var eventNumber in new[]{0,1})foreach(var m in raceTeamA.Item2.OrderBy(x=>x.Round).ThenBy(x=>x.Position)){
       var a=teams.Single(x=>x.Players[0].UserId==m.PlayerA!.UserId);var b=teams.Single(x=>x.Players[0].UserId==m.PlayerB!.UserId);
       var avgA=a.Players.Average(x=>serialPlayers[x.UserId]);var avgB=b.Players.Average(x=>serialPlayers[x.UserId]);
       var oldTeamA=serialTeams[a.TeamId!];var oldTeamB=serialTeams[b.TeamId!];
       serialTeams[a.TeamId!]=EloCalculator.Next(oldTeamA,oldTeamB,true);serialTeams[b.TeamId!]=EloCalculator.Next(oldTeamB,oldTeamA,false);
       foreach(var p in a.Players)serialPlayers[p.UserId]=EloCalculator.Next(serialPlayers[p.UserId],avgB,true);
       foreach(var p in b.Players)serialPlayers[p.UserId]=EloCalculator.Next(serialPlayers[p.UserId],avgA,false);
    }
    await Task.WhenAll(economy.Close(raceTeamA.Item1.Id,"moderator",default),independentEconomy.Close(raceTeamB.Item1.Id,"moderator",default));
    var playerResults=true;foreach(var pair in serialPlayers)playerResults&=(await Rating(pair.Key))!.Rating==pair.Value;
    var teamResults=true;foreach(var pair in serialTeams)teamResults&=(await Rating(pair.Key,"Valorant","team"))!.Rating==pair.Value;
    Check(playerResults&&teamResults,"independent concurrent team closures equal serial player and team ratings");
    Check(await HistoryCount(raceTeamA.Item1.Id)==84&&await HistoryCount(raceTeamB.Item1.Id)==84,"independent concurrent teams retain both full histories");
    var existingFailure=await Finished(teams,"Valorant");await elo.Prepare(existingFailure.Item1.Id,default);
    var existingHash=await PersistedHash();
    await db.RunCommandAsync<BsonDocument>(new BsonDocument{{"collMod","EloAdjustments"},{"validator",new BsonDocument("RuleVersion",new BsonDocument("$lt",0))}});
    try { await economy.Close(existingFailure.Item1.Id,"moderator",default);throw new Exception("Expected failure"); }catch(MongoWriteException){}
    Check(existingHash==await PersistedHash(),"independent failed team closure preserves existing ratings wallets histories and markers");
    await db.RunCommandAsync<BsonDocument>(new BsonDocument{{"collMod","EloAdjustments"},{"validator",new BsonDocument()}});
    var readHash=await PersistedHash();await controller.PlayerHistory(ObjectId.GenerateNewId().ToString(),"Valorant");await controller.Teams("Valorant");
    Check(readHash==await PersistedHash(),"independent public history and rankings do not provision or mutate entities");
    Console.WriteLine($"{checks} Elo checks passed against disposable MongoDB");
}
finally { await db.Client.DropDatabaseAsync(db.DatabaseNamespace.DatabaseName); }
