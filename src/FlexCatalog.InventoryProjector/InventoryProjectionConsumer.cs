using System.Text.Json;
using FlexCatalog.Contracts.Eventing;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace FlexCatalog.InventoryProjector;

/// <summary>
/// The independent consumer proving the event-streaming pattern end to end
/// (ADR 0006): subscribes to every event under the shared subject prefix,
/// and maintains its own MongoDB read-model (<see cref="ProductProjection"/>)
/// built entirely from the event stream -- it never queries
/// FlexCatalog.Api's `products` collection. Reacts observably differently
/// from the API itself: an `InventoryAdjusted` event that drops quantity to
/// zero or below is logged as a distinct out-of-stock warning, which the
/// API's own write path has no equivalent of.
///
/// Subscribes via a durable JetStream consumer rather than core NATS
/// (ADR 0008): the publish side (FlexCatalog.Api's
/// DomainEventPublishingService) is untouched plain core `PublishAsync`,
/// but this consumer's own durable name lets it resume from wherever it
/// last acknowledged if it was down when events published, instead of
/// losing them.
/// </summary>
public sealed class InventoryProjectionConsumer(
    INatsConnection connection,
    IMongoCollection<ProductProjection> projections,
    IOptions<NatsOptions> natsOptions,
    ILogger<InventoryProjectionConsumer> logger) : BackgroundService
{
    /// <summary>
    /// After this many delivery attempts, a message is treated as poison
    /// (ADR 0008) -- logged as a dead letter and terminated rather than
    /// redelivered forever.
    /// </summary>
    public const int MaxDeliverAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = natsOptions.Value;
        var subject = $"{options.SubjectPrefix}.>";
        var js = connection.CreateJetStreamContext();

        // Same rationale as the pre-JetStream version of this loop: any of
        // stream/consumer setup or the connection itself can fail if NATS
        // isn't reachable yet (e.g. containers starting in parallel), and
        // that must retry with backoff rather than take the host down via
        // BackgroundServiceExceptionBehavior.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await js.CreateOrUpdateStreamAsync(
                    new StreamConfig(options.StreamName, [subject]), stoppingToken);

                var consumer = await js.CreateOrUpdateConsumerAsync(
                    options.StreamName,
                    new ConsumerConfig(options.DurableConsumerName)
                    {
                        AckPolicy = ConsumerConfigAckPolicy.Explicit,
                        DeliverPolicy = ConsumerConfigDeliverPolicy.All,
                        MaxDeliver = MaxDeliverAttempts,
                        AckWait = TimeSpan.FromSeconds(30),
                        FilterSubject = subject,
                    },
                    stoppingToken);

                logger.LogInformation(
                    "Consuming {Subject} via durable consumer {Consumer} on stream {Stream}",
                    subject, options.DurableConsumerName, options.StreamName);

                await foreach (var msg in consumer.ConsumeAsync<string>(cancellationToken: stoppingToken))
                {
                    await ProcessAsync(msg, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Subscription to {Subject} failed; retrying in 5s.", subject);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    /// <summary>
    /// Processes one JetStream message and resolves its ack: success acks,
    /// a transient failure naks for redelivery, and a failure that has
    /// already exhausted <see cref="MaxDeliverAttempts"/> is dead-lettered
    /// (logged, then terminated so it stops redelivering) rather than
    /// retried forever (ADR 0008).
    /// </summary>
    internal async Task ProcessAsync(INatsJSMsg<string?> msg, CancellationToken ct)
    {
        try
        {
            await HandleAsync(msg.Data, ct);
            await msg.AckAsync(cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var deliveryCount = msg.Metadata?.NumDelivered ?? 1;
            if (deliveryCount >= MaxDeliverAttempts)
            {
                logger.LogError(
                    ex,
                    "Dead-lettering message on subject {Subject} after {DeliveryCount} delivery attempts. Envelope: {Envelope}",
                    msg.Subject, deliveryCount, msg.Data);
                await msg.AckTerminateAsync(cancellationToken: ct);
            }
            else
            {
                logger.LogError(
                    ex,
                    "Failed to process message on subject {Subject} (attempt {DeliveryCount}/{MaxDeliverAttempts}); will redeliver.",
                    msg.Subject, deliveryCount, MaxDeliverAttempts);
                await msg.NakAsync(cancellationToken: ct);
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
            case EventTypes.ProductUpdated:
                await HandleProductUpdatedAsync(envelope, ct);
                break;
            case EventTypes.InventoryAdjusted:
                await HandleInventoryAdjustedAsync(envelope, ct);
                break;
            case EventTypes.ProductDeleted:
                await HandleProductDeletedAsync(envelope, ct);
                break;
            default:
                logger.LogDebug("Ignoring unrecognized event type {EventType}", envelope.EventType);
                break;
        }
    }

    /// <summary>
    /// Pure envelope-to-projection mapping, exposed `internal` (via
    /// InternalsVisibleTo, mirroring
    /// SearchIndexingConsumer.BuildDocumentFromCreated's rationale) so the
    /// mapping logic is unit-testable without a live MongoDB -- only the
    /// fact that it persists correctly needs the integration test.
    /// </summary>
    internal static ProductProjection BuildProjectionFromCreated(DomainEventEnvelope envelope)
    {
        var payload = JsonSerializer.Deserialize<ProductCreatedPayload>(envelope.PayloadJson)
            ?? throw new InvalidOperationException($"'{envelope.EventType}' payload failed to deserialize.");

        return new ProductProjection
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
    }

    /// <summary>
    /// Same shape as <see cref="BuildProjectionFromCreated"/> -- ProductUpdated
    /// carries a full current snapshot (like ProductCreated), not a diff, so
    /// a rename/re-categorize/inventory edit can be applied as a full
    /// upsert-replace instead of a targeted field update.
    /// </summary>
    internal static ProductProjection BuildProjectionFromUpdated(DomainEventEnvelope envelope)
    {
        var payload = JsonSerializer.Deserialize<ProductUpdatedPayload>(envelope.PayloadJson)
            ?? throw new InvalidOperationException($"'{envelope.EventType}' payload failed to deserialize.");

        return new ProductProjection
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
    }

    /// <summary>
    /// The projection id to delete on <see cref="EventTypes.ProductDeleted"/> --
    /// a deleted product must not keep surfacing in this read-model, the
    /// same reasoning as SearchIndexingConsumer.BuildDeletionFromEnvelope.
    /// </summary>
    internal static string BuildDeletionIdFromEnvelope(DomainEventEnvelope envelope)
    {
        var payload = JsonSerializer.Deserialize<ProductDeletedPayload>(envelope.PayloadJson)
            ?? throw new InvalidOperationException($"'{envelope.EventType}' payload failed to deserialize.");

        return ProductProjection.ProjectionId(envelope.TenantId, payload.ProductId);
    }

    private async Task HandleProductCreatedAsync(DomainEventEnvelope envelope, CancellationToken ct)
    {
        var projection = BuildProjectionFromCreated(envelope);

        await projections.ReplaceOneAsync(
            p => p.Id == projection.Id,
            projection,
            new ReplaceOptions { IsUpsert = true },
            ct);

        logger.LogInformation(
            "Projected {EventType}: {Sku} (tenant {TenantId}), quantity {Quantity}",
            envelope.EventType, projection.Sku, envelope.TenantId, projection.QuantityOnHand);
    }

    private async Task HandleProductUpdatedAsync(DomainEventEnvelope envelope, CancellationToken ct)
    {
        var projection = BuildProjectionFromUpdated(envelope);

        await projections.ReplaceOneAsync(
            p => p.Id == projection.Id,
            projection,
            new ReplaceOptions { IsUpsert = true },
            ct);

        logger.LogInformation(
            "Projected {EventType}: {Sku} (tenant {TenantId}), quantity {Quantity}",
            envelope.EventType, projection.Sku, envelope.TenantId, projection.QuantityOnHand);
    }

    private async Task HandleProductDeletedAsync(DomainEventEnvelope envelope, CancellationToken ct)
    {
        var id = BuildDeletionIdFromEnvelope(envelope);

        await projections.DeleteOneAsync(p => p.Id == id, ct);

        logger.LogInformation("Removed projection {Id} (tenant {TenantId})", id, envelope.TenantId);
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
