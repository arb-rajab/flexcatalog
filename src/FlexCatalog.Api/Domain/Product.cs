using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FlexCatalog.Api.Domain;

public sealed class Product
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Tenant discriminator. Every query against this collection MUST be
    /// scoped by this field -- see Repositories/ProductRepository.cs and
    /// docs/project-memory/decisions/0001-multi-tenant-isolation-strategy.md.
    /// </summary>
    [BsonElement("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    [BsonElement("sku")]
    public string Sku { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("categoryType")]
    [BsonRepresentation(BsonType.String)]
    public CategoryType CategoryType { get; set; }

    [BsonElement("price")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Price { get; set; }

    [BsonElement("currency")]
    public string Currency { get; set; } = "USD";

    [BsonElement("inStock")]
    public bool InStock { get; set; }

    [BsonElement("quantityOnHand")]
    public int QuantityOnHand { get; set; }

    [BsonElement("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Category-specific attribute bag. Polymorphic -- see ProductAttributes.cs.
    /// </summary>
    [BsonElement("attributes")]
    public ProductAttributes Attributes { get; set; } = null!;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
