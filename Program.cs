using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using ProyectAPI.Infrastructure;
using ProyectAPI.Repositories;
using ProyectAPI.Services;
using System.IO;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.None;
    options.HttpOnly = HttpOnlyPolicy.Always;
    options.Secure = CookieSecurePolicy.Always;
});

var keysFolder = Path.Combine(builder.Environment.ContentRootPath, "DataProtectionKeys");
Directory.CreateDirectory(keysFolder);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("ProyectAPI")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(14));

builder.Services.AddSingleton<MongoDbContext>();

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITournamentRepository, TournamentRepository>();
builder.Services.AddScoped<ITournamentService, TournamentService>();

// Registro de dependencias para el Módulo de Competencia (llaves y enfrentamientos)
builder.Services.AddScoped<IMatchRepository, MatchRepository>();
builder.Services.AddHostedService<BracketSchedulerService>();
builder.Services.AddScoped<IMatchStatsService, MatchStatsService>();

// Registro de dependencias para el Módulo de Equipos
builder.Services.AddScoped<ITeamRepository, TeamRepository>();
builder.Services.AddScoped<TeamService>();

// Registro de dependencias para el Módulo de Rankings
builder.Services.AddScoped<RankingService>();

builder.Services.AddControllers();

// Configuración de CORS para el Frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173", "http://localhost:4200", "http://127.0.0.1:5500")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Configuración de Swagger con botón "Authorize" para JWT
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Introduce 'Bearer' [espacio] y luego tu token."
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
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = "Cookies";
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
    .AddCookie("Cookies", options =>
    {
        options.Cookie.Name = "ExternalAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.None;
    })
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

        // Leer el token desde la cookie "access_token".
        // Si no existe la cookie, se usa el header Authorization (Swagger, pruebas, etc.)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue("access_token", out var token))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            }
        };
    })
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"] ?? string.Empty;
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? string.Empty;
        options.SignInScheme = "Cookies";
        options.CallbackPath = "/api/auth/external/google/callback";
        options.SaveTokens = true;
        options.CorrelationCookie.Name = "GoogleCorrelation";
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.CorrelationCookie.HttpOnly = true;
        options.CorrelationCookie.Path = "/";
        options.CorrelationCookie.IsEssential = true;
        options.CorrelationCookie.Expiration = TimeSpan.FromMinutes(10);
        options.Events.OnRemoteFailure = context =>
        {
            context.HandleResponse();
            context.Response.Redirect("http://localhost:5173/login?error=external_auth_failed");
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Activar CORS (Debe ir antes de autenticación y controladores)
app.UseCookiePolicy();

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
