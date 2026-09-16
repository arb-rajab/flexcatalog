using FlexCatalog.Api.Auth;
using FlexCatalog.Api.Domain;
using FlexCatalog.Api.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FlexCatalog.Api.Repositories;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default);

    Task InsertAsync(Product product, CancellationToken ct = default);

    Task<bool> ReplaceAsync(Product product, CancellationToken ct = default);

    Task<bool> DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Runs a caller-built $facet-style aggregation pipeline (expected to
    /// yield exactly one output document) with a tenant $match stage
    /// forcibly prepended. This is the ONLY way search/facet queries reach
    /// the collection, so a caller cannot construct a pipeline that skips
    /// tenant scoping even by accident.
    /// </summary>
    Task<BsonDocument?> AggregateTenantScopedAsync(IEnumerable<BsonDocument> pipelineStages, CancellationToken ct = default);
}

public sealed class ProductRepository(MongoContext context, ITenantContext tenantContext) : IProductRepository
{
    private IMongoCollection<Product> Collection => context.Products;

    private FilterDefinition<Product> TenantFilter =>
        Builders<Product>.Filter.Eq(p => p.TenantId, tenantContext.TenantId);

    public async Task<Product?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var filter = TenantFilter & Builders<Product>.Filter.Eq(p => p.Id, id);
        return await Collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default)
    {
        var filter = TenantFilter & Builders<Product>.Filter.Eq(p => p.Sku, sku);
        return await Collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task InsertAsync(Product product, CancellationToken ct = default)
    {
        // Never trust a caller-supplied tenantId on write; always stamp the
        // authenticated caller's own tenant.
        product.TenantId = tenantContext.TenantId;
        await Collection.InsertOneAsync(product, cancellationToken: ct);
    }

    public async Task<bool> ReplaceAsync(Product product, CancellationToken ct = default)
    {
        product.TenantId = tenantContext.TenantId;
        var filter = TenantFilter & Builders<Product>.Filter.Eq(p => p.Id, product.Id);
        var result = await Collection.ReplaceOneAsync(filter, product, cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var filter = TenantFilter & Builders<Product>.Filter.Eq(p => p.Id, id);
        var result = await Collection.DeleteOneAsync(filter, ct);
        return result.DeletedCount > 0;
    }

    public async Task<BsonDocument?> AggregateTenantScopedAsync(IEnumerable<BsonDocument> pipelineStages, CancellationToken ct = default)
    {
        var tenantMatch = new BsonDocument("$match",
            new BsonDocument("tenantId", tenantContext.TenantId));

        var stages = new List<BsonDocument> { tenantMatch };
        stages.AddRange(pipelineStages);

        PipelineDefinition<Product, BsonDocument> pipeline = stages.ToArray();
        using var cursor = await Collection.AggregateAsync(pipeline, cancellationToken: ct);
        var results = await cursor.ToListAsync(ct);
        return results.FirstOrDefault();
    }
}
