using System.Text.Json;
using FlexCatalog.Contracts.Eventing;
using FlexCatalog.InventoryProjector;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MongoDB.Driver;
using NATS.Client.Core;

namespace FlexCatalog.UnitTests.InventoryProjector;

/// <summary>
/// Regression coverage for the bug where InventoryProjectionConsumer's
/// event-type switch only recognized ProductCreated/InventoryAdjusted:
/// ProductUpdated and ProductDeleted (added alongside SearchIndexer, ADR
/// 0007) fell through to the "unrecognized event type" default and were
/// silently dropped, leaving the read-model stale after a rename or
/// delete. HandleAsync is exercised end to end against a mocked
/// IMongoCollection so the assertion is "the projection collection was
/// actually written to", not just "the mapping function produced the
/// right shape" -- the switch statement itself is what regressed.
/// </summary>
public class InventoryProjectionConsumerTests
{
    private static DomainEventEnvelope Envelope<TPayload>(string eventType, string tenantId, TPayload payload) => new(
        EventId: "evt-1",
        EventType: eventType,
        TenantId: tenantId,
        OccurredAtUtc: DateTime.UtcNow,
        PayloadJson: JsonSerializer.Serialize(payload));

    private static FlexCatalog.InventoryProjector.InventoryProjectionConsumer CreateConsumer(
        Mock<IMongoCollection<ProductProjection>> collection) => new(
        Mock.Of<INatsConnection>(),
        collection.Object,
        Options.Create(new NatsOptions { Url = "nats://localhost:4222", SubjectPrefix = "flexcatalog.events" }),
        Mock.Of<ILogger<FlexCatalog.InventoryProjector.InventoryProjectionConsumer>>());

    [Fact]
    public async Task HandleAsync_ProductUpdated_UpsertsProjectionInsteadOfBeingDropped()
    {
        var collection = new Mock<IMongoCollection<ProductProjection>>();
        collection
            .Setup(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<ProductProjection>>(),
                It.IsAny<ProductProjection>(),
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplaceOneResult.Acknowledged(1, 1, null));

        var consumer = CreateConsumer(collection);

        var payload = new ProductUpdatedPayload(
            ProductId: "p1", Sku: "SKU-1", Name: "Renamed Widget", CategoryType: "Electronics",
            Price: 19.99m, Currency: "USD", QuantityOnHand: 7, InStock: true,
            Description: null, Tags: [], Attributes: new Dictionary<string, List<string>>());
        var envelope = Envelope(EventTypes.ProductUpdated, "tenant-a", payload);

        await consumer.HandleAsync(JsonSerializer.Serialize(envelope), CancellationToken.None);

        collection.Verify(
            c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<ProductProjection>>(),
                It.Is<ProductProjection>(p =>
                    p.Id == "tenant-a:p1" && p.Name == "Renamed Widget" && p.LastEventType == EventTypes.ProductUpdated),
                It.Is<ReplaceOptions>(o => o.IsUpsert),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ProductDeleted_RemovesProjectionInsteadOfBeingDropped()
    {
        var collection = new Mock<IMongoCollection<ProductProjection>>();
        collection
            .Setup(c => c.DeleteOneAsync(
                It.IsAny<FilterDefinition<ProductProjection>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteResult.Acknowledged(1));

        var consumer = CreateConsumer(collection);

        var payload = new ProductDeletedPayload(ProductId: "p1", Sku: "SKU-1");
        var envelope = Envelope(EventTypes.ProductDeleted, "tenant-a", payload);

        await consumer.HandleAsync(JsonSerializer.Serialize(envelope), CancellationToken.None);

        collection.Verify(
            c => c.DeleteOneAsync(It.IsAny<FilterDefinition<ProductProjection>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void BuildProjectionFromUpdated_MapsRenamedFieldsOntoProjection()
    {
        var payload = new ProductUpdatedPayload(
            ProductId: "p1", Sku: "SKU-1", Name: "New Name", CategoryType: "Books",
            Price: 9.99m, Currency: "USD", QuantityOnHand: 3, InStock: true,
            Description: "desc", Tags: ["fiction"], Attributes: new Dictionary<string, List<string>>());
        var envelope = Envelope(EventTypes.ProductUpdated, "tenant-a", payload);

        var projection = FlexCatalog.InventoryProjector.InventoryProjectionConsumer.BuildProjectionFromUpdated(envelope);

        Assert.Equal("tenant-a:p1", projection.Id);
        Assert.Equal("New Name", projection.Name);
        Assert.Equal("Books", projection.CategoryType);
        Assert.Equal(3, projection.QuantityOnHand);
        Assert.True(projection.InStock);
        Assert.Equal(EventTypes.ProductUpdated, projection.LastEventType);
    }

    [Fact]
    public void BuildDeletionIdFromEnvelope_ReturnsTenantScopedProjectionId()
    {
        var payload = new ProductDeletedPayload(ProductId: "p1", Sku: "SKU-1");
        var envelope = Envelope(EventTypes.ProductDeleted, "tenant-a", payload);

        var id = FlexCatalog.InventoryProjector.InventoryProjectionConsumer.BuildDeletionIdFromEnvelope(envelope);

        Assert.Equal("tenant-a:p1", id);
    }
}
