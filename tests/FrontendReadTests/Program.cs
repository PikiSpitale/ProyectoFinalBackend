using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.Extensions.Options;
using ProyectAPI.Infrastructure;
using ProyectAPI.Models;
using ProyectAPI.Repositories;
using ProyectAPI.Services;
var checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
async Task Error(string code,Func<Task> call){try{await call();}catch(WalletOperationException e){Check(e.Code==code,code);return;}throw new Exception("Expected "+code);}
var uri=Environment.GetEnvironmentVariable("TEST_MONGODB_URI")??throw new Exception("Local disposable Mongo required");
var context=new MongoDbContext(Options.Create(new MongoDbSettings{ConnectionString=uri,DatabaseName="frontend_reads_"+Guid.NewGuid().ToString("N")}));
var db=context.Database;
try{
var users=new UserRepository(context);var actor=new User{Username="actor",Email="actor@example.invalid"};await users.CreateAsync(actor,default);
var outsider=new User{Username="other",Email="other@example.invalid"};await users.CreateAsync(outsider,default);
var service=new FrontendReadService(new FrontendReadRepository(context));var ranking=new RankingService(new EloRepository(context));
var t=new Tournament{Id=ObjectId.GenerateNewId().ToString(),Title="one",GameName="Valorant",Enrollments=new(){new TournamentEnrollment{Id="mine",Players=new(){new TournamentPayer{UserId=actor.Id,Username="actor",DebitId="private-debit"}}},new TournamentEnrollment{Id="other",Players=new(){new TournamentPayer{UserId=outsider.Id}}}}};
await db.GetCollection<Tournament>("Tournaments").InsertOneAsync(t);
var inbox=await service.Enrollments(actor.Id,"Pending",1,1,default);
Check(inbox.Items.Count==1 && inbox.Items[0].RequestId=="mine" && !inbox.HasMore,"inbox filters actor before paging");
Check(inbox.Items[0].TournamentId==t.Id && inbox.Items[0].GameName=="Valorant","inbox links tournament metadata");
Check(!System.Text.Json.JsonSerializer.Serialize(inbox).Contains("private-debit"),"inbox omits debit references");
var t2=new Tournament{Id=ObjectId.GenerateNewId().ToString(),Title="two",CreatedAt=t.CreatedAt.AddMinutes(1),Enrollments=new(){new TournamentEnrollment{Id="second",Players=new(){new TournamentPayer{UserId=actor.Id}}}}};await db.GetCollection<Tournament>("Tournaments").InsertOneAsync(t2);
inbox=await service.Enrollments(actor.Id,null,1,1,default);Check(inbox.HasMore&&inbox.Items[0].RequestId=="second","stable newest first with hasMore");
Check((await service.Enrollments(actor.Id,null,2,1,default)).Items.Single().RequestId=="mine","second inbox page");
Check((await service.Enrollments(outsider.Id,null,1,20,default)).Items.Single().RequestId=="other","separate user inbox");
Check((await service.Enrollments(actor.Id,"Cancelled",1,20,default)).Items.Count==0,"status filter");
await Error("invalid_status",()=>service.Enrollments(actor.Id,"anything",1,20,default));await Error("invalid_pagination",()=>service.Enrollments(actor.Id,null,0,20,default));
var missing=await ranking.Current("player",actor.Id,"  valorant ",default);Check(missing.Rating==null&&missing.Position==null,"unrated is not invented");
var ids=Enumerable.Range(0,4).Select(_=>ObjectId.GenerateNewId().ToString()).OrderBy(x=>x).ToArray();
var rows=new[]{new EloRating{Id="Valorant:"+ids[0],EntityId=ids[0],Name="Alpha",NameSort="alpha",GameName="Valorant",Kind="player",Rating=1200},new EloRating{Id="Valorant:"+ids[1],EntityId=ids[1],Name="Beta",NameSort="beta",GameName="Valorant",Kind="player",Rating=1000},new EloRating{Id="Valorant:"+ids[2],EntityId=ids[2],Name="Beta",NameSort="beta",GameName="Valorant",Kind="player",Rating=1000},new EloRating{Id="LoL:"+ids[3],EntityId=ids[3],Name="Ignored",NameSort="ignored",GameName="League of Legends",Kind="player",Rating=9999}};
await db.GetCollection<EloRating>("PlayerEloRatings").InsertManyAsync(rows);
var current=await ranking.Current("player",ids[2],"Valorant",default);Check(current.Position==3&&current.Rating==1000,"position uses same game and deterministic ties");
Check((await ranking.Current("team",ids[2],"Valorant",default)).Rating==null,"team and player ratings separated");
await db.GetCollection<EloRating>("TeamEloRatings").InsertOneAsync(new EloRating{Id="Valorant:"+ids[2],EntityId=ids[2],GameName="Valorant",Kind="team",Rating=1400});
Check((await ranking.Current("team",ids[2],"Valorant",default)).Position==1,"team current position");
await Error("invalid_game",()=>ranking.Current("player",actor.Id,"bad",default));await Error("invalid_user",()=>ranking.Current("player","bad","Valorant",default));
await db.GetCollection<User>("users").UpdateOneAsync(x=>x.Id==actor.Id,Builders<User>.Update.Set(x=>x.IsActive,false));await Error("inactive_user",()=>service.Enrollments(actor.Id,null,1,20,default));
var type=typeof(ProyectAPI.Controllers.MyTournamentEnrollmentsController);Check(type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute),true).Length==1,"inbox requires authentication");
await db.GetCollection<User>("users").UpdateOneAsync(x=>x.Id==actor.Id,Builders<User>.Update.Set(x=>x.IsActive,true));
var controller=new ProyectAPI.Controllers.MyTournamentEnrollmentsController(service){ControllerContext=new Microsoft.AspNetCore.Mvc.ControllerContext{HttpContext=new Microsoft.AspNetCore.Http.DefaultHttpContext{User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]{new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier,outsider.Id)},"test"))}}};
var response=(Microsoft.AspNetCore.Mvc.OkObjectResult)await controller.Get();
Check(((ProyectAPI.DTOs.MyEnrollmentsPage)response.Value!).Items.Single().RequestId=="other","HTTP inbox derives owner from claims");
controller.ControllerContext.HttpContext.User=new System.Security.Claims.ClaimsPrincipal();
Check(await controller.Get() is Microsoft.AspNetCore.Mvc.ObjectResult{StatusCode:401},"missing claim rejected");
await Error("invalid_pagination",()=>service.Enrollments(actor.Id,null,int.MaxValue,100,default));
await Error("invalid_pagination",()=>service.Enrollments(actor.Id,null,1,101,default));
Check((await service.Enrollments(actor.Id," pending ",1,20,default)).Items.Count==2,"status normalization");
var many=Enumerable.Range(0,55).Select(i=>new EloRating{Id="Valorant:"+ObjectId.GenerateNewId(),EntityId=ObjectId.GenerateNewId().ToString(),NameSort="ahead",GameName="Valorant",Kind="player",Rating=2000});await db.GetCollection<EloRating>("PlayerEloRatings").InsertManyAsync(many);
Check((await ranking.Current("player",ids[2],"Valorant",default)).Position==58,"exact rank beyond first leaderboard page");
async Task<string> Snapshot(){var text="";foreach(var name in new[]{"users","Tournaments","PlayerEloRatings","TeamEloRatings"})text+=string.Join("",(await db.GetCollection<BsonDocument>(name).Find(new BsonDocument()).Sort(new BsonDocument("_id",1)).ToListAsync()).Select(x=>x.ToJson()));return text;}
var before=await Snapshot();await service.Enrollments(actor.Id,null,1,20,default);await ranking.Current("player",ids[2],"Valorant",default);Check(before==await Snapshot(),"reads preserve persisted state");
// Independent probes: per-request filtering and snapshot coherence under writes.
var compound=new Tournament{Id=ObjectId.GenerateNewId().ToString(),Title="compound",CreatedAt=t2.CreatedAt.AddMinutes(1),Enrollments=new(){
 new(){Id="a-cancelled",Status="Cancelled",Players=new(){new(){UserId=actor.Id}}},
 new(){Id="b-admitted",Status="Admitted",Players=new(){new(){UserId=outsider.Id}}},
 new(){Id="c-cancelled",Status="Cancelled",Players=new(){new(){UserId=actor.Id},new(){UserId=outsider.Id}}}
}};await db.GetCollection<Tournament>("Tournaments").InsertOneAsync(compound);
var cancelled1=await service.Enrollments(actor.Id,"Cancelled",1,1,default);var cancelled2=await service.Enrollments(actor.Id,"Cancelled",2,1,default);
Check(cancelled1.Items.Single().RequestId=="a-cancelled"&&cancelled1.HasMore&&cancelled2.Items.Single().RequestId=="c-cancelled"&&!cancelled2.HasMore,"independent per-request status and actor filters precede pagination");
Check((await service.Enrollments(outsider.Id,"Cancelled",1,20,default)).Items.Single().RequestId=="c-cancelled","independent teammate sees shared request only");
controller.ControllerContext.HttpContext.User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]{new System.Security.Claims.Claim("sub",actor.Id)},"test"));
controller.ControllerContext.HttpContext.Request.QueryString=new Microsoft.AspNetCore.Http.QueryString("?userId="+outsider.Id);
var mine=(Microsoft.AspNetCore.Mvc.OkObjectResult)await controller.Get("Admitted");
Check(((ProyectAPI.DTOs.MyEnrollmentsPage)mine.Value!).Items.Count==0,"independent query userId cannot replace sub claim");
var ratingCollection=db.GetCollection<EloRating>("PlayerEloRatings");var moving=ObjectId.GenerateNewId().ToString();var rival=ObjectId.GenerateNewId().ToString();
await ratingCollection.InsertManyAsync(new[]{new EloRating{Id="CS:GO:"+moving,EntityId=moving,GameName="CS:GO",Kind="player",NameSort="moving",Rating=1000},new EloRating{Id="CS:GO:"+rival,EntityId=rival,GameName="CS:GO",Kind="player",NameSort="rival",Rating=1500}});
async Task Writer(){using var session=await db.Client.StartSessionAsync();for(var i=0;i<60;i++)await session.WithTransactionAsync(async(s,ct)=>{await ratingCollection.UpdateOneAsync(s,x=>x.EntityId==moving,Builders<EloRating>.Update.Set(x=>x.Rating,i%2==0?2000:1000),cancellationToken:ct);return true;});}
async Task Reader(){for(var i=0;i<60;i++){var row=await ranking.Current("player",moving,"CS:GO",default);if(!(row.Rating==2000&&row.Position==1||row.Rating==1000&&row.Position==2))throw new Exception("mixed rating/position snapshot");}}
await Task.WhenAll(Writer(),Reader());Check(true,"independent current rating and position remain coherent during concurrent updates");
Check((await db.GetCollection<Tournament>("Tournaments").Indexes.ListAsync()).ToList().Any(x=>x["name"]=="tournament_enrollment_inbox"),"inbox actor/order index exists");
Console.WriteLine($"{checks} frontend read checks passed");
}finally{await db.Client.DropDatabaseAsync(db.DatabaseNamespace.DatabaseName);}
