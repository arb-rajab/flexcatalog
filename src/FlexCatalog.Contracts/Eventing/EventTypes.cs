namespace FlexCatalog.Contracts.Eventing;

/// <summary>
/// The `eventType` values carried on <see cref="DomainEventEnvelope"/> and used
/// verbatim as the last subject token (`flexcatalog.events.&lt;eventType&gt;`).
/// Shared between the publisher (FlexCatalog.Api) and the consumer
/// (FlexCatalog.InventoryProjector) so both sides switch on the same literals.
/// </summary>
public static class EventTypes
{
    public const string ProductCreated = "product.created";

    public const string InventoryAdjusted = "inventory.adjusted";
}
