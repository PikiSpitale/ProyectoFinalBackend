using ProyectAPI.Infrastructure;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(); // <-- NUEVO: Registra Swagger

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();   // <-- NUEVO: Genera el archivo Swagger JSON
    app.UseSwaggerUI(); // <-- NUEVO: Habilita la interfaz gráfica web
}

app.UseHttpsRedirection();

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

app.Run();