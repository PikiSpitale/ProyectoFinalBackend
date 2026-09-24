using ProyectAPI.Infrastructure;
var builder = WebApplication.CreateBuilder(args);


//Database connection

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

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.UseHttpsRedirection();

//Temporal connection

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
