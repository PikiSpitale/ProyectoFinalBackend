using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ProyectAPI.Infrastructure;

public sealed class MongoDbContext
{
    public IMongoDatabase Database { get; }

    public MongoDbContext(IOptions<MongoDbSettings> options)
    {
        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            throw new InvalidOperationException(
                "No se configuró MongoDbSettings:ConnectionString."
            );
        }

        if (string.IsNullOrWhiteSpace(settings.DatabaseName))
        {
            throw new InvalidOperationException(
                "No se configuró MongoDbSettings:DatabaseName."
            );
        }

        var client = new MongoClient(settings.ConnectionString);

        Database = client.GetDatabase(settings.DatabaseName);
    }

    public async Task PingAsync(
        CancellationToken cancellationToken = default
    )
    {
        await Database.RunCommandAsync<BsonDocument>(
            new BsonDocument("ping", 1),
            cancellationToken: cancellationToken
        );
    }
}