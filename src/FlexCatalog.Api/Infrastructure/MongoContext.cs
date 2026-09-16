using FlexCatalog.Api.Domain;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace FlexCatalog.Api.Infrastructure;

/// <summary>
/// Single place that knows collection names. Repositories depend on this
/// instead of calling IMongoDatabase.GetCollection directly, so a renamed
/// collection is a one-line change.
/// </summary>
public sealed class MongoContext
{
    public MongoContext(IMongoClient client, IOptions<MongoOptions> options)
    {
        Database = client.GetDatabase(options.Value.DatabaseName);
    }

    public IMongoDatabase Database { get; }

    public IMongoCollection<Product> Products => Database.GetCollection<Product>("products");

    public IMongoCollection<Tenant> Tenants => Database.GetCollection<Tenant>("tenants");

    public IMongoCollection<User> Users => Database.GetCollection<User>("users");
}
