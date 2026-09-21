using System.Text.Json;
using FlexCatalog.Contracts.Eventing;
using FlexCatalog.SearchIndexer;

namespace FlexCatalog.UnitTests.SearchIndexer;

/// <summary>
/// Tests the pure envelope-to-Meilisearch-document mapping logic (internal,
/// exposed via InternalsVisibleTo, mirroring
/// ProductSearchServiceQueryBuildingTests' rationale) without needing a live
/// Meilisearch or NATS -- only the fact that this indexes correctly against
/// a real broker/engine needs the integration test.
/// </summary>
public class SearchIndexingConsumerMappingTests
{
    private static DomainEventEnvelope Envelope<TPayload>(string eventType, string tenantId, TPayload payload) => new(
        EventId: "evt-1",
        EventType: eventType,
        TenantId: tenantId,
        OccurredAtUtc: DateTime.UtcNow,
        PayloadJson: JsonSerializer.Serialize(payload));

    [Fact]
    public void BuildDocumentFromCreated_MapsFlattenedAttributesToDiscreteFields()
    {
        var payload = new ProductCreatedPayload(
            ProductId: "p1",
            Sku: "SKU-1",
            Name: "Wireless Headphones",
            CategoryType: "Electronics",
            Price: 99.99m,
            Currency: "USD",
            QuantityOnHand: 10,
            InStock: true,
            Description: "Noise-cancelling",
            Tags: ["audio", "wireless"],
            Attributes: new Dictionary<string, List<string>> { ["brand"] = ["Acme"] });

        var envelope = Envelope(EventTypes.ProductCreated, "tenant-a", payload);

        var document = FlexCatalog.SearchIndexer.SearchIndexingConsumer.BuildDocumentFromCreated(envelope);

        Assert.Equal("tenant-a_p1", document.Id);
        Assert.Equal("tenant-a", document.TenantId);
        Assert.Equal("p1", document.ProductId);
        Assert.Equal("SKU-1", document.Sku);
        Assert.Equal("Wireless Headphones", document.Name);
        Assert.Equal("Noise-cancelling", document.Description);
        Assert.Equal("Electronics", document.CategoryType);
        Assert.Equal(99.99m, document.Price);
        Assert.True(document.InStock);
        Assert.Equal(10, document.QuantityOnHand);
        Assert.Equal(["audio", "wireless"], document.Tags);
        Assert.Equal("Acme", document.Brand);
        Assert.Null(document.Sizes);
        Assert.Null(document.Colors);
        Assert.Null(document.Author);
    }

    [Fact]
    public void BuildDocumentFromCreated_MapsApparelSizesAndColors()
    {
        var payload = new ProductCreatedPayload(
            "p2", "SKU-2", "Jacket", "Apparel", 49.99m, "USD", 3, true,
            null, [], new Dictionary<string, List<string>> { ["sizes"] = ["M", "L"], ["colors"] = ["Black"] });

        var envelope = Envelope(EventTypes.ProductCreated, "tenant-a", payload);

        var document = FlexCatalog.SearchIndexer.SearchIndexingConsumer.BuildDocumentFromCreated(envelope);

        Assert.Equal(["M", "L"], document.Sizes);
        Assert.Equal(["Black"], document.Colors);
        Assert.Null(document.Brand);
    }

    [Fact]
    public void BuildDocumentFromUpdated_MapsAuthorAttribute()
    {
        var payload = new ProductUpdatedPayload(
            "p3", "SKU-3", "Some Novel", "Books", 14.99m, "USD", 20, true,
            "A book", ["fiction"], new Dictionary<string, List<string>> { ["author"] = ["Jane Doe"] });

        var envelope = Envelope(EventTypes.ProductUpdated, "tenant-b", payload);

        var document = FlexCatalog.SearchIndexer.SearchIndexingConsumer.BuildDocumentFromUpdated(envelope);

        Assert.Equal("tenant-b_p3", document.Id);
        Assert.Equal("Jane Doe", document.Author);
        Assert.Equal("Books", document.CategoryType);
    }

    [Fact]
    public void BuildStockUpdateFromInventoryAdjusted_ProducesPartialDocumentWithNewQuantity()
    {
        var payload = new InventoryAdjustedPayload(
            ProductId: "p1", Sku: "SKU-1", Delta: -5, PreviousQuantity: 10, NewQuantity: 5, InStock: true);

        var envelope = Envelope(EventTypes.InventoryAdjusted, "tenant-a", payload);

        var update = FlexCatalog.SearchIndexer.SearchIndexingConsumer.BuildStockUpdateFromInventoryAdjusted(envelope);

        var json = JsonSerializer.Serialize(update);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("tenant-a_p1", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal(5, doc.RootElement.GetProperty("quantityOnHand").GetInt32());
        Assert.True(doc.RootElement.GetProperty("inStock").GetBoolean());
    }

    [Fact]
    public void BuildDeletionFromEnvelope_ReturnsTenantScopedDocumentId()
    {
        var payload = new ProductDeletedPayload(ProductId: "p1", Sku: "SKU-1");

        var envelope = Envelope(EventTypes.ProductDeleted, "tenant-a", payload);

        var documentId = FlexCatalog.SearchIndexer.SearchIndexingConsumer.BuildDeletionFromEnvelope(envelope);

        Assert.Equal("tenant-a_p1", documentId);
    }
}
