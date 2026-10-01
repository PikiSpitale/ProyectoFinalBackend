using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using ProyectAPI.Infrastructure;
using ProyectAPI.Repositories;
using ProyectAPI.Services;
using System.Text;

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

// Configuración de tu base de datos
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

// Configuración de CORS para el Frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173", "http://localhost:4200", "http://127.0.0.1:5500")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Configuración de Swagger con botón "Authorize" para JWT
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Pega tu Token JWT aquí."
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Configuración de seguridad JWT
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
    app.UseSwagger();
    app.UseSwaggerUI(); // Limpio y funcionando con Swashbuckle
}

app.UseHttpsRedirection();

// Activar CORS (Debe ir antes de autenticación y controladores)
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

// Endpoint para probar la conexión con MongoDB
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

app.Run();