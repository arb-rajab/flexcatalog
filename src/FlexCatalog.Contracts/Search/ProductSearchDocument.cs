namespace FlexCatalog.Contracts.Search;

/// <summary>
/// The Meilisearch document shape (ADR 0007). Shared between
/// FlexCatalog.SearchIndexer (the only writer -- see
/// <c>SearchIndexingConsumer</c>) and FlexCatalog.Api (a read-only client
/// that deserializes search hits), the same reason
/// <see cref="Eventing.DomainEventEnvelope"/> lives here rather than being
/// redefined by each side: one source of truth for the wire shape instead
/// of two structurally-identical types drifting apart.
///
/// <see cref="Id"/> is "{tenantId}_{productId}" -- gives every tenant's
/// products distinct primary keys in the one shared index, the same
/// discriminator-based idea as FlexCatalog.InventoryProjector.ProductProjection's
/// own id scheme (see ADR 0007's tenant-isolation section; ADR 0001 chose
/// the same shared-collection-plus-discriminator shape for MongoDB) --
/// but with `_` rather than `:` as the separator, because unlike a MongoDB
/// `_id`, a Meilisearch document id may only contain letters, digits,
/// hyphens, and underscores; a colon is rejected outright
/// (`invalid_document_id`).
///
/// <see cref="Brand"/>/<see cref="Sizes"/>/<see cref="Colors"/>/
/// <see cref="Author"/> are the category-specific attributes flattened out
/// to top-level, individually filterable/searchable fields -- the same four
/// names as ProductSearchService.FacetedAttributeFields, so the two search
/// paths describe the same underlying vocabulary even though they query
/// different engines.
/// </summary>
public sealed class ProductSearchDocument
{
    public string Id { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string ProductId { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string CategoryType { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public string Currency { get; set; } = "USD";

    public bool InStock { get; set; }

    public int QuantityOnHand { get; set; }

    public List<string> Tags { get; set; } = [];

    public string? Brand { get; set; }

    public List<string>? Sizes { get; set; }

    public List<string>? Colors { get; set; }

    public string? Author { get; set; }

    public static string DocumentId(string tenantId, string productId) => $"{tenantId}_{productId}";
}
