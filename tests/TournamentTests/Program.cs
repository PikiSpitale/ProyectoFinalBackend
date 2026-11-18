using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Bson;
using ProyectAPI.Models;
using ProyectAPI.Infrastructure;
using ProyectAPI.Repositories;
using ProyectAPI.Services;
using ProyectAPI.DTOs;
var uri=Environment.GetEnvironmentVariable("TEST_MONGODB_URI")??throw new Exception("TEST_MONGODB_URI required");
var context=new MongoDbContext(Options.Create(new MongoDbSettings{ConnectionString=uri,DatabaseName="tournament_tests_"+Guid.NewGuid().ToString("N")}));
var db=context.Database;var checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);checks++;}
async Task Reject(Func<Task> action,string name){try{await action();}catch(InvalidOperationException){Check(true,name);return;}catch(WalletOperationException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
var users=new UserRepository(context);var wallet=new WalletRepository(context);var repo=new TournamentEconomyRepository(context);var clock=new Clock();var economy=new TournamentEconomyService(repo,wallet,clock,new EloService(new EloRepository(context),clock));
var tournaments=new TournamentRepository(context);var matchRepo=new MatchRepository(context);
var service=new TournamentService(tournaments,matchRepo,new TeamRepository(context),users,economy);
var stats=new MatchStatsService(tournaments,matchRepo,economy);
async Task<User> User(){var user=new User{Username=Guid.NewGuid().ToString("N"),Email=Guid.NewGuid()+"@example.invalid",Games=new(){new(){GameName="Valorant",InGameName="test",Rank="Oro 1"}}};await users.CreateAsync(user,default);return user;}
async Task<Tournament> Event(bool teams=false,bool free=false){var t=new Tournament{Id=ObjectId.GenerateNewId().ToString(),Title="test",GameName="Valorant",RankConditions="Sin restricción",StartDate=clock.Now.AddMinutes(10),EndDate=clock.Now.AddHours(1),EconomyVersion=1,MaxParticipants=8,TournamentType=teams?"Equipo":"Individual",IsFree=free,EntryPoints=free?0:250};await tournaments.CreateAsync(t,default);return t;}
TournamentParticipantDto Dto(User p)=>new(p.Id,"forged","forged","forged",DateTime.UtcNow);
async Task<(Team,List<User>)> Team(){var players=new List<User>();for(var i=0;i<5;i++)players.Add(await User());var team=new Team{Id=ObjectId.GenerateNewId().ToString(),Name="team",GameName="Valorant",CaptainId=players[0].Id,Members=players.Select(x=>new TeamMember{UserId=x.Id,Username=x.Username,IsStarter=true}).ToList()};await db.GetCollection<Team>("Teams").InsertOneAsync(team);return(team,players);}
try{
var t=await Event();var player=await User();
await service.RegisterParticipantAsync(t.Id,Dto(player),default);await service.RegisterParticipantAsync(t.Id,Dto(player),default);
Check(await wallet.BalanceAsync(player.Id,default)==750,"single entry debit and retry");
Check((await tournaments.GetByIdAsync(t.Id,default))!.Participants[0].Username==player.Username,"profile used instead of client data");
await Reject(()=>service.GenerateBracketAsync(t.Id,default),"cannot start early");
clock.Now=t.StartDate;await service.GenerateDueBracketsAsync(default);
Check(await wallet.BalanceAsync(player.Id,default)==1000,"incomplete event refunds at start");
Check((await tournaments.GetByIdAsync(t.Id,default))!.Status=="Cancelado","incomplete event cancelled");
await economy.Cancel(t.Id,default);Check(await wallet.BalanceAsync(player.Id,default)==1000,"repeat cancellation idempotent");
// Rejected spending rolls back admission, and identity/roster rules are enforced.
var insufficient=await Event();var poor=await User();
await wallet.TransactionAsync(async(w,ct)=>{await w.DebitAsync(poor.Id,900,"test","test","test",ct);return true;},default);
await Reject(()=>service.RegisterParticipantAsync(insufficient.Id,Dto(poor),default),"insufficient balance rejected");
Check((await tournaments.GetByIdAsync(insufficient.Id,default))!.Participants.Count==0 && await wallet.BalanceAsync(poor.Id,default)==100,"failed admission leaves points and seats unchanged");
var ranked=await Event();await db.GetCollection<Tournament>("Tournaments").UpdateOneAsync(x=>x.Id==ranked.Id,Builders<Tournament>.Update.Set(x=>x.RankConditions,"Mínimo: Platino 1"));
await Reject(()=>service.RegisterParticipantAsync(ranked.Id,Dto(player),default),"minimum rank enforced");
Check(await wallet.BalanceAsync(player.Id,default)==1000,"rank rejection does not charge");
// Two pending teams race for the last seat.
var teamEvent=await Event(true);var admittedTeams=new List<(Team,List<User>)>();
for(var i=0;i<9;i++)admittedTeams.Add(await Team());
for(var i=0;i<9;i++)await service.RegisterTeamAsync(teamEvent.Id,admittedTeams[i].Item1.Id,admittedTeams[i].Item2[0].Id,default);
Check((await tournaments.GetByIdAsync(teamEvent.Id,default))!.Participants.Count==0,"pending teams reserve no seats");
var firstRequest=(await tournaments.GetByIdAsync(teamEvent.Id,default))!.Enrollments[0];
await Reject(()=>economy.Confirm(teamEvent.Id,firstRequest.Id,player.Id,default),"outsider cannot confirm another wallet");
var overlap=new Team{Id=ObjectId.GenerateNewId().ToString(),Name="overlap",GameName="Valorant",CaptainId=admittedTeams[0].Item2[1].Id,Members=admittedTeams[0].Item1.Members};await db.GetCollection<Team>("Teams").InsertOneAsync(overlap);
await Reject(()=>service.RegisterTeamAsync(teamEvent.Id,overlap.Id,overlap.CaptainId,default),"same players cannot enter another team");
Check(await wallet.BalanceAsync(overlap.CaptainId,default)==1000,"rejected overlapping roster does not charge");
for(var i=0;i<7;i++){
var e=(await tournaments.GetByIdAsync(teamEvent.Id,default))!.Enrollments.Single(x=>x.TeamId==admittedTeams[i].Item1.Id);
foreach(var p in admittedTeams[i].Item2.Skip(1))await economy.Confirm(teamEvent.Id,e.Id,p.Id,default);
}
for(var i=7;i<9;i++){
var e=(await tournaments.GetByIdAsync(teamEvent.Id,default))!.Enrollments.Single(x=>x.TeamId==admittedTeams[i].Item1.Id);
foreach(var p in admittedTeams[i].Item2.Skip(1).Take(3))await economy.Confirm(teamEvent.Id,e.Id,p.Id,default);
}
var ready=(await tournaments.GetByIdAsync(teamEvent.Id,default))!.Enrollments.Where(x=>x.Status=="Pending").ToList();
await Task.WhenAll(ready.Select(e=>economy.Confirm(teamEvent.Id,e.Id,e.Players[4].UserId,default)));
var after=(await tournaments.GetByIdAsync(teamEvent.Id,default))!;
Check(after.Participants.Count==8,"last seat race keeps capacity eight");
var cancelled=after.Enrollments.Single(x=>x.Status=="Cancelled");
foreach(var p in cancelled.Players)Check(await wallet.BalanceAsync(p.UserId,default)==1000,"losing pending team fully refunded");
await economy.Cancel(teamEvent.Id,default);
foreach(var e in after.Enrollments)foreach(var p in e.Players)Check(await wallet.BalanceAsync(p.UserId,default)==1000,"team cancellation refunds each payer");
// Completing a full team event pays all five fixed starters, not only captain.
var teamPaid=await Event(true);
foreach(var tuple in admittedTeams.Take(8)){
 await service.RegisterTeamAsync(teamPaid.Id,tuple.Item1.Id,tuple.Item2[0].Id,default);
 var e=(await tournaments.GetByIdAsync(teamPaid.Id,default))!.Enrollments.Single(x=>x.TeamId==tuple.Item1.Id);
 foreach(var p in tuple.Item2.Skip(1))await economy.Confirm(teamPaid.Id,e.Id,p.Id,default);
}
clock.Now=teamPaid.StartDate;await service.GenerateBracketAsync(teamPaid.Id,default);
for(var round=1;round<=3;round++)foreach(var m in (await matchRepo.GetByTournamentAsync(teamPaid.Id,default)).Where(x=>x.Round==round))await service.SetMatchResultAsync(teamPaid.Id,m.Id,new MatchResultRequest(13,0,m.PlayerA!.UserId),default);
var teamFinal=(await matchRepo.GetByTournamentAsync(teamPaid.Id,default)).Single(x=>x.Round==3);
await economy.Close(teamPaid.Id,"moderator",default);
foreach(var p in admittedTeams.Single(x=>x.Item1.CaptainId==teamFinal.WinnerUserId).Item2)Check(await wallet.BalanceAsync(p.Id,default)==1750,"champion team pays every starter");
// Full bracket, immutable closure and direct awards.
var paid=await Event();var entrants=new List<User>();for(var i=0;i<8;i++){var p=await User();entrants.Add(p);await service.RegisterParticipantAsync(paid.Id,Dto(p),default);}
clock.Now=paid.StartDate;await service.GenerateBracketAsync(paid.Id,default);
for(var round=1;round<=3;round++)foreach(var m in (await matchRepo.GetByTournamentAsync(paid.Id,default)).Where(x=>x.Round==round))await service.SetMatchResultAsync(paid.Id,m.Id,new MatchResultRequest(13,0,m.PlayerA!.UserId),default);
var final=(await matchRepo.GetByTournamentAsync(paid.Id,default)).Single(x=>x.Round==3);
await Task.WhenAll(economy.Close(paid.Id,"moderator",default),economy.Close(paid.Id,"moderator",default));
Check(await wallet.BalanceAsync(final.WinnerUserId!,default)==1750,"champion paid once at definitive closure");
var total=0L;foreach(var p in entrants)total+=await wallet.BalanceAsync(p.Id,default);Check(total==8000,"paid event preserves 2000 pool");
await Reject(()=>service.SetMatchResultAsync(paid.Id,final.Id,new MatchResultRequest(0,13,final.PlayerB!.UserId),default),"closed result frozen");
await Reject(()=>service.AdjustMatchScoreAsync(paid.Id,final.Id,new MatchScoreRequest("A",1),default),"closed score frozen");
await Reject(()=>service.DisqualifyParticipantAsync(paid.Id,final.PlayerA!.UserId,"test",default),"closed disqualification frozen");
await Reject(()=>stats.UpdateStatAsync(paid.Id,final.Id,new MatchStatRequest("A","kills",1),default),"closed stats frozen");
await Reject(()=>economy.Cancel(paid.Id,default),"closed cancellation refused");
await Reject(()=>service.DeleteTournamentAsync(paid.Id,default),"closed delete refused");
// Free points cap across different closures on same Córdoba day.
var freeIds=new List<string>();
for(var run=0;run<3;run++){
var free=await Event(false,true);foreach(var p in entrants)await service.RegisterParticipantAsync(free.Id,Dto(p),default);
clock.Now=free.StartDate;await service.GenerateBracketAsync(free.Id,default);
for(var round=1;round<=3;round++)foreach(var m in (await matchRepo.GetByTournamentAsync(free.Id,default)).Where(x=>x.Round==round))await service.SetMatchResultAsync(free.Id,m.Id,new MatchResultRequest(13,0,m.PlayerA!.UserId),default);
freeIds.Add(free.Id);
}
await Task.WhenAll(freeIds.Select(id=>economy.Close(id,"moderator",default)));
foreach(var p in entrants){var credits=await db.GetCollection<PointTransaction>("PointTransactions").Find(x=>x.UserId==p.Id&&x.Reason=="tournament_prize").ToListAsync();var freeSum=credits.Where(x=>x.Reference!=paid.Id).Sum(x=>x.Amount);Check(freeSum<=200&&freeSum>=150,"Córdoba daily free cap");}
// Legacy empty records adapt explicitly without retroactive billing.
var legacy=await Event();await db.GetCollection<Tournament>("Tournaments").UpdateOneAsync(x=>x.Id==legacy.Id,Builders<Tournament>.Update.Set(x=>x.EconomyVersion,0).Set(x=>x.MaxParticipants,16));
await economy.Run(legacy.Id,(t,w,ct)=>Task.FromResult(true),default);
Check((await tournaments.GetByIdAsync(legacy.Id,default))!.MaxParticipants==8,"empty legacy adaptation");
Check(TournamentEconomyRepository.Session==null,"transaction session does not leak");
var controller=typeof(ProyectAPI.Controllers.TournamentEconomyController);
Check(controller.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute),true).Length==1,"economy routes require authentication");
foreach(var name in new[]{"Close","Cancel","Adapt"})Check(controller.GetMethod(name)!.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute),true).Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single().Roles==AppRoles.Staff,"staff only "+name);
var httpController=new ProyectAPI.Controllers.TournamentEconomyController(economy){ControllerContext=new Microsoft.AspNetCore.Mvc.ControllerContext{HttpContext=new Microsoft.AspNetCore.Http.DefaultHttpContext{User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]{new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier,player.Id)},"test"))}}};
Check(await httpController.Requests(t.Id,default) is Microsoft.AspNetCore.Mvc.OkObjectResult,"request query uses authenticated claims");
// Independent verifier probes: immutable rejection state, claims, and retained roster.
async Task<string> HashState(){
 var result="";
 foreach(var name in new[]{"Tournaments","Matches","Wallets","PointTransactions","TournamentFreeDaily"}) {
  var docs=await db.GetCollection<BsonDocument>(name).Find(new BsonDocument()).Sort(new BsonDocument("_id",1)).ToListAsync();
  result+=name+string.Join("",docs.Select(x=>x.ToJson()));
 }
 return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(result)));
}
var hash=await HashState();
await Reject(()=>service.SetMatchResultAsync(paid.Id,final.Id,new MatchResultRequest(0,13,final.PlayerB!.UserId),default),"independent closed mutation rejection");
Check(hash==await HashState(),"rejected closed operation preserves all persisted bytes");
var claimEvent=await Event();var claimant=await User();var forged=await User();
var claimsController=new ProyectAPI.Controllers.TournamentsController(service){ControllerContext=new Microsoft.AspNetCore.Mvc.ControllerContext{HttpContext=new Microsoft.AspNetCore.Http.DefaultHttpContext{User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]{new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier,claimant.Id)},"test"))}}};
await claimsController.Register(claimEvent.Id,Dto(forged),default);
Check(await wallet.BalanceAsync(claimant.Id,default)==750&&await wallet.BalanceAsync(forged.Id,default)==1000,"HTTP register debits claims owner only");
Check((await tournaments.GetByIdAsync(claimEvent.Id,default))!.Participants.Single().UserId==claimant.Id,"HTTP register ignores forged body user");
var fixedEvent=await Event(true);var original=await Team();
await service.RegisterTeamAsync(fixedEvent.Id,original.Item1.Id,original.Item2[0].Id,default);
var fixedRequest=(await tournaments.GetByIdAsync(fixedEvent.Id,default))!.Enrollments.Single();
await db.GetCollection<Team>("Teams").DeleteOneAsync(x=>x.Id==original.Item1.Id);
foreach(var member in original.Item2.Skip(1))await economy.Confirm(fixedEvent.Id,fixedRequest.Id,member.Id,default);
Check((await tournaments.GetByIdAsync(fixedEvent.Id,default))!.Enrollments.Single().Status=="Admitted","fixed roster survives source team deletion");
hash=await HashState();await Reject(()=>economy.Confirm(fixedEvent.Id,fixedRequest.Id,claimant.Id,default),"outsider confirmation rejected after admission");
Check(hash==await HashState(),"unauthorized command preserves all persisted bytes");
Console.WriteLine($"{checks} tournament checks passed");
}finally{await db.Client.DropDatabaseAsync(db.DatabaseNamespace.DatabaseName);}
sealed class Clock:TimeProvider{public DateTime Now=DateTime.UtcNow;public override DateTimeOffset GetUtcNow()=>new(Now,TimeSpan.Zero);}
