namespace FlexCatalog.Api.Eventing;

/// <summary>
/// Fire-and-forget publication of a domain event (ADR 0005).
/// <see cref="Publish{TPayload}"/> is synchronous and non-blocking by
/// design -- it only ever touches an in-memory queue, never the network --
/// so it is always safe to call after a write has already succeeded,
/// without a CancellationToken and without the caller awaiting anything.
/// </summary>
public interface IDomainEventPublisher
{
    void Publish<TPayload>(string eventType, string tenantId, TPayload payload);
}
