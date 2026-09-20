using System.Text.Json;
using FlexCatalog.Contracts.Eventing;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NATS.Client.Core;

namespace FlexCatalog.InventoryProjector;

/// <summary>
/// The independent consumer proving the event-streaming pattern end to end
/// (ADR 0005): subscribes to every event under the shared subject prefix,
/// and maintains its own MongoDB read-model (<see cref="ProductProjection"/>)
/// built entirely from the event stream -- it never queries
/// FlexCatalog.Api's `products` collection. Reacts observably differently
/// from the API itself: an `InventoryAdjusted` event that drops quantity to
/// zero or below is logged as a distinct out-of-stock warning, which the
/// API's own write path has no equivalent of.
/// </summary>
public sealed class InventoryProjectionConsumer(
    INatsConnection connection,
    IMongoCollection<ProductProjection> projections,
    IOptions<NatsOptions> natsOptions,
    ILogger<InventoryProjectionConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subject = $"{natsOptions.Value.SubjectPrefix}.>";

        // NatsConnection.SubscribeAsync connects eagerly: if NATS is down
        // when this starts (e.g. the two containers in docker-compose start
        // in parallel), the very first MoveNextAsync throws instead of
        // waiting to connect lazily. Without this loop, that exception
        // would propagate out of ExecuteAsync and, under the default
        // BackgroundServiceExceptionBehavior, take the whole host down --
        // a broker hiccup killing the process is not the resilience this
        // consumer is meant to demonstrate. Retry with a fixed backoff
        // until NATS is reachable or the host is stopping.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                logger.LogInformation("Subscribing to {Subject}", subject);
                await foreach (var msg in connection.SubscribeAsync<string>(subject, cancellationToken: stoppingToken))
                {
                    try
                    {
                        await HandleAsync(msg.Data, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to process message on subject {Subject}", msg.Subject);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Subscription to {Subject} failed; retrying in 5s.", subject);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    internal async Task HandleAsync(string? json, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        var envelope = JsonSerializer.Deserialize<DomainEventEnvelope>(json);
        if (envelope is null)
        {
            return;
        }

        switch (envelope.EventType)
        {
            case EventTypes.ProductCreated:
                await HandleProductCreatedAsync(envelope, ct);
                break;
            case EventTypes.InventoryAdjusted:
                await HandleInventoryAdjustedAsync(envelope, ct);
                break;
            default:
                logger.LogDebug("Ignoring unrecognized event type {EventType}", envelope.EventType);
                break;
        }
    }

    private async Task HandleProductCreatedAsync(DomainEventEnvelope envelope, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<ProductCreatedPayload>(envelope.PayloadJson)
            ?? throw new InvalidOperationException($"'{envelope.EventType}' payload failed to deserialize.");

        var projection = new ProductProjection
        {
            Id = ProductProjection.ProjectionId(envelope.TenantId, payload.ProductId),
            TenantId = envelope.TenantId,
            ProductId = payload.ProductId,
            Sku = payload.Sku,
            Name = payload.Name,
            CategoryType = payload.CategoryType,
            QuantityOnHand = payload.QuantityOnHand,
            InStock = payload.InStock,
            LastEventType = envelope.EventType,
            LastEventAtUtc = envelope.OccurredAtUtc,
        };

        await projections.ReplaceOneAsync(
            p => p.Id == projection.Id,
            projection,
            new ReplaceOptions { IsUpsert = true },
            ct);

        logger.LogInformation(
            "Projected {EventType}: {Sku} (tenant {TenantId}), quantity {Quantity}",
            envelope.EventType, payload.Sku, envelope.TenantId, payload.QuantityOnHand);
    }

    private async Task HandleInventoryAdjustedAsync(DomainEventEnvelope envelope, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<InventoryAdjustedPayload>(envelope.PayloadJson)
            ?? throw new InvalidOperationException($"'{envelope.EventType}' payload failed to deserialize.");

        var id = ProductProjection.ProjectionId(envelope.TenantId, payload.ProductId);
        var update = Builders<ProductProjection>.Update
            .Set(p => p.TenantId, envelope.TenantId)
            .Set(p => p.ProductId, payload.ProductId)
            .Set(p => p.Sku, payload.Sku)
            .Set(p => p.QuantityOnHand, payload.NewQuantity)
            .Set(p => p.InStock, payload.InStock)
            .Set(p => p.LastEventType, envelope.EventType)
            .Set(p => p.LastEventAtUtc, envelope.OccurredAtUtc);

        await projections.UpdateOneAsync(p => p.Id == id, update, new UpdateOptions { IsUpsert = true }, ct);

        if (payload.NewQuantity <= 0)
        {
            logger.LogWarning(
                "Out-of-stock alert: {Sku} (tenant {TenantId}) reached quantity {Quantity}",
                payload.Sku, envelope.TenantId, payload.NewQuantity);
        }
        else
        {
            logger.LogInformation(
                "Projected {EventType}: {Sku} (tenant {TenantId}) {Previous} -> {New}",
                envelope.EventType, payload.Sku, envelope.TenantId, payload.PreviousQuantity, payload.NewQuantity);
        }
    }
}
