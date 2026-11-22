using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using MongoDB.Bson;
using MongoDB.Driver;
using ProyectAPI.Controllers;
using ProyectAPI.Services;
using ProyectAPI.DTOs;
using ProyectAPI.Infrastructure;
using ProyectAPI.Repositories;
using System.Security.Cryptography;
var n=0;void Check(bool x,string s){if(!x)throw new Exception(s);Console.WriteLine("PASS "+s);n++;}
var dbName="verify_auth_"+Guid.NewGuid().ToString("N");
var context=new MongoDbContext(Options.Create(new MongoDbSettings{ConnectionString=Environment.GetEnvironmentVariable("TEST_MONGODB_URI") ?? throw new Exception("Local disposable Mongo required"),DatabaseName=dbName}));
var db=context.Database;
try {
var users=new UserRepository(context);var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Jwt:Key"]=new string('t',64),["Jwt:Issuer"]="verify",["Jwt:Audience"]="verify"}).Build();var auth=new AuthService(users,config);
var account=await auth.RegisterAsync(new RegisterRequest{Username="Exact.User",Email="exact@example.invalid",Password="Password123!"},default);
async Task<string> Hash(){var all=new List<string>();foreach(var c in (await (await db.ListCollectionNamesAsync()).ToListAsync()).Order()){var docs=await db.GetCollection<BsonDocument>(c).Find(new BsonDocument()).ToListAsync();all.Add(c+string.Join("",docs.Select(x=>x.ToJson()).Order()));}return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("",all))));}
AuthController Controller()=>new(auth,users,config){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}};
var before=await Hash();
foreach(var req in new[]{new LoginRequest{Username="Exact.User",Password="bad"},new LoginRequest{Username="missing",Password="Password123!"},new LoginRequest{Username=".*",Password="Password123!"},new LoginRequest{Username="exact.user",Password="Password123!"},new LoginRequest{Email="missing@example.invalid",Username="Exact.User",Password="Password123!"}}){var c=Controller();var result=await c.Login(req,default);Check(result is UnauthorizedObjectResult && !c.Response.Headers.ContainsKey("Set-Cookie"),"failed credentials return401 without cookie: "+req.Username);}
Check(await Hash()==before,"rejected logins preserve every stored document");
var emailController=Controller();var emailResult=await emailController.Login(new LoginRequest{Email=" EXACT@EXAMPLE.INVALID ",Username="missing",Password="Password123!"},default);Check(emailResult is OkObjectResult,"normalized email takes priority");
var cookie=emailController.Response.Headers.SetCookie.ToString();Check(cookie.Contains("httponly")&&cookie.Contains("secure")&&cookie.Contains("samesite=none"),"email cookie retains security flags");
var data=((OkObjectResult)emailResult).Value!;Check(data.GetType().GetProperty("Token")==null&&data.GetType().GetProperty("token")==null,"HTTP JSON excludes bearer token");
Check(Attribute.IsDefined(typeof(AuthController).GetMethod("Me")!,typeof(AuthorizeAttribute)),"me still requires authenticated principal");
{var c=Controller();var result=await c.Login(new LoginRequest{Username=" Exact.User ",Password="Password123!"},default);Check(result is OkObjectResult,"trimmed username authenticates");var v=c.Response.Headers.SetCookie.ToString();Check(v.Contains("httponly")&&v.Contains("secure")&&v.Contains("samesite=none"),"username cookie security matches email");var raw=v.Split(';')[0].Split('=',2)[1];var jwt=new JwtSecurityTokenHandler().ReadJwtToken(raw);Check(jwt.Claims.Any(x=>(x.Type=="nameid"||x.Type==ClaimTypes.NameIdentifier)&&x.Value==account.User.Id),"username JWT binds registered identity");Check(jwt.Claims.Any(x=>(x.Type=="role"||x.Type==ClaimTypes.Role)&&x.Value==account.User.Role),"username JWT preserves role");}
Check(await Hash()==before,"all successful logins preserve every stored document");Console.WriteLine(n+" independent checks");
}finally{await db.Client.DropDatabaseAsync(dbName);}
