using System.Text.Json;
using FlexCatalog.Contracts.Eventing;

namespace FlexCatalog.Api.Eventing;

public sealed class ChannelDomainEventPublisher(
    DomainEventChannel channel,
    ILogger<ChannelDomainEventPublisher> logger) : IDomainEventPublisher
{
    public void Publish<TPayload>(string eventType, string tenantId, TPayload payload)
    {
        var envelope = new DomainEventEnvelope(
            EventId: Guid.NewGuid().ToString("n"),
            EventType: eventType,
            TenantId: tenantId,
            OccurredAtUtc: DateTime.UtcNow,
            PayloadJson: JsonSerializer.Serialize(payload));

        // TryWrite never blocks; DropOldest means a queue backlog (which a
        // single always-draining background reader should never produce in
        // this workload) sheds old events rather than applying backpressure
        // to the caller. Either way this can never fail the write it follows.
        if (!channel.Writer.TryWrite(envelope))
        {
            logger.LogWarning(
                "Dropped domain event {EventType} for tenant {TenantId}: publish queue rejected the write.",
                eventType, tenantId);
        }
    }
}
