using ProyectAPI.Infrastructure;
using MongoDB.Driver;
using Microsoft.Extensions.Configuration;
using ProyectAPI.Repositories;
using ProyectAPI.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer; // <-- NUEVO: Para JWT
using Microsoft.IdentityModel.Tokens;                // <-- NUEVO: Para JWT
using System.Text;                                   // <-- NUEVO: Para JWT

var builder = WebApplication.CreateBuilder(args);

// DEBUG: mostrar valores efectivos de la sección MongoDbSettings (temporal)
var section = builder.Configuration.GetSection(MongoDbSettings.SectionName);
var effectiveSettings = section.Get<MongoDbSettings>() ?? new MongoDbSettings();
string maskedConn = string.IsNullOrWhiteSpace(effectiveSettings.ConnectionString)
    ? "<vacío>"
    : $"[length={effectiveSettings.ConnectionString.Length}]";
Console.WriteLine($"[DEBUG] MongoDbSettings.Section: {MongoDbSettings.SectionName}");
Console.WriteLine($"[DEBUG] ConnectionString: {maskedConn}");
Console.WriteLine($"[DEBUG] DatabaseName: {(string.IsNullOrWhiteSpace(effectiveSettings.DatabaseName) ? "<vacío>" : effectiveSettings.DatabaseName)}");

// Configuración de tu base de datos (tu código original)
builder.Services
    .AddOptions<MongoDbSettings>()
    .Bind(builder.Configuration.GetSection(MongoDbSettings.SectionName))
    .Validate(
        settings =>
            !string.IsNullOrWhiteSpace(settings.ConnectionString)
            && !string.IsNullOrWhiteSpace(settings.DatabaseName),
        "La configuración de MongoDB está incompleta."
    )
    .ValidateOnStart();

builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// <-- NUEVO: Configuración de seguridad JWT (Debe ir ANTES de builder.Build)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(builder.Configuration["Jwt:Key"]!)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"]
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "v1");
    });
}

app.UseHttpsRedirection();

// <-- NUEVO: Activa la seguridad en las peticiones web (Debe ir ANTES de MapControllers)
app.UseAuthentication();
app.UseAuthorization();

// Tu endpoint original para probar la conexión
app.MapGet(
    "/health/mongodb",
    async (
        MongoDbContext mongoDbContext,
        CancellationToken cancellationToken
    ) =>
    {
        try
        {
            await mongoDbContext.PingAsync(cancellationToken);

            return Results.Ok(new
            {
                status = "ok",
                database = "MongoDB Atlas conectado"
            });
        }
        catch
        {
            return Results.Problem(
                title: "No se pudo conectar con MongoDB Atlas",
                statusCode: StatusCodes.Status503ServiceUnavailable
            );
        }
    }
);

app.MapControllers();
app.Run(); // <-- CORREGIDO: Se eliminó el app.Run() duplicado