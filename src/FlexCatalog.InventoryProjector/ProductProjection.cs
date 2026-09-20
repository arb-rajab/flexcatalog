using MongoDB.Bson.Serialization.Attributes;

namespace FlexCatalog.InventoryProjector;

/// <summary>
/// The read-model this consumer builds purely from the events it receives --
/// it never reads FlexCatalog.Api's own `products` collection. Lives in its
/// own MongoDB collection ("productInventoryProjection") in the same
/// database, which is what makes it observably a *projection derived from
/// the event stream* rather than a copy of the source-of-truth data (ADR
/// 0006). Deliberately holds only the fields the two events carry -- this is
/// a demonstration read-model, not a replacement for `GET /api/products/{id}`.
/// </summary>
public sealed class ProductProjection
{
    /// <summary>"{tenantId}:{productId}" -- events for the same product always
    /// resolve to the same document, without needing a compound index.</summary>
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    [BsonElement("productId")]
    public string ProductId { get; set; } = string.Empty;

    [BsonElement("sku")]
    public string Sku { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("categoryType")]
    public string CategoryType { get; set; } = string.Empty;

    [BsonElement("quantityOnHand")]
    public int QuantityOnHand { get; set; }

    [BsonElement("inStock")]
    public bool InStock { get; set; }

    [BsonElement("lastEventType")]
    public string LastEventType { get; set; } = string.Empty;

    [BsonElement("lastEventAtUtc")]
    public DateTime LastEventAtUtc { get; set; }

    public static string ProjectionId(string tenantId, string productId) => $"{tenantId}:{productId}";
}
