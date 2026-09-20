namespace FlexCatalog.Contracts.Eventing;

/// <summary>
/// Wire format for every message published to the `flexcatalog.events.&gt;`
/// subject hierarchy (ADR 0005). <see cref="PayloadJson"/> is itself JSON,
/// shaped per <see cref="EventTypes"/> -- kept as an embedded string rather
/// than a polymorphic property so the envelope's own schema never changes as
/// event-specific payloads are added, and a subscriber can always read
/// eventId/eventType/tenantId/occurredAtUtc before deciding whether (and how)
/// to deserialize the payload.
/// </summary>
public sealed record DomainEventEnvelope(
    string EventId,
    string EventType,
    string TenantId,
    DateTime OccurredAtUtc,
    string PayloadJson);
