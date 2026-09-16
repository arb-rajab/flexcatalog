using FlexCatalog.Api.Domain;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;

namespace FlexCatalog.Api.Infrastructure;

/// <summary>
/// Creates indexes idempotently at startup (CreateOneAsync is a no-op if an
/// equivalent index already exists). For a portfolio-scale demo this is
/// simpler and more honest than a separate migration tool -- there is
/// nothing to migrate, only indexes to ensure exist.
///
/// See docs/project-memory/decisions/0002-mongodb-schema-and-indexing.md for
/// the reasoning behind each index.
/// </summary>
public sealed class MongoIndexInitializer(MongoContext context, ILogger<MongoIndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await CreateProductIndexesAsync(cancellationToken);
        await CreateUserIndexesAsync(cancellationToken);
        await CreateTenantIndexesAsync(cancellationToken);
        logger.LogInformation("MongoDB indexes ensured");
    }

    private async Task CreateProductIndexesAsync(CancellationToken cancellationToken)
    {
        var products = context.Products;
        var keys = Builders<Product>.IndexKeys;

        var indexes = new List<CreateIndexModel<Product>>
        {
            // Every product lookup by SKU is scoped to a tenant; SKUs are
            // unique per tenant, not globally.
            new(keys.Ascending(p => p.TenantId).Ascending(p => p.Sku),
                new CreateIndexOptions { Unique = true, Name = "tenant_sku_unique" }),

            // Supports the common "browse a category, sorted/filtered by
            // price" query shape used by the faceted search endpoint.
            new(keys.Ascending(p => p.TenantId).Ascending(p => p.CategoryType).Ascending(p => p.Price),
                new CreateIndexOptions { Name = "tenant_category_price" }),

            new(keys.Ascending(p => p.TenantId).Ascending(p => p.InStock),
                new CreateIndexOptions { Name = "tenant_instock" }),

            // Compound wildcard index: indexes every field under
            // "attributes" (brand, sizes, author, ...) without knowing the
            // per-category shape in advance. This is what makes
            // category-specific attribute filters in the faceted search
            // endpoint fast without a fixed, hand-maintained index per
            // field per category.
            new(keys.Ascending(p => p.TenantId).Ascending("attributes.$**"),
                new CreateIndexOptions { Name = "tenant_attributes_wildcard" }),

            // Free-text search across name/description, still tenant-scoped.
            new(keys.Ascending(p => p.TenantId).Text(p => p.Name).Text(p => p.Description),
                new CreateIndexOptions { Name = "tenant_text_search" }),
        };

        await products.Indexes.CreateManyAsync(indexes, cancellationToken);
    }

    private async Task CreateUserIndexesAsync(CancellationToken cancellationToken)
    {
        var keys = Builders<User>.IndexKeys;
        await context.Users.Indexes.CreateOneAsync(
            new CreateIndexModel<User>(keys.Ascending(u => u.Username),
                new CreateIndexOptions { Unique = true, Name = "username_unique" }),
            cancellationToken: cancellationToken);
    }

    private async Task CreateTenantIndexesAsync(CancellationToken cancellationToken)
    {
        var keys = Builders<Tenant>.IndexKeys;
        await context.Tenants.Indexes.CreateOneAsync(
            new CreateIndexModel<Tenant>(keys.Ascending(t => t.Slug),
                new CreateIndexOptions { Unique = true, Name = "slug_unique" }),
            cancellationToken: cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
