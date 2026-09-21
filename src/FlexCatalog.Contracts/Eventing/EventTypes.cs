namespace FlexCatalog.Contracts.Eventing;

/// <summary>
/// The `eventType` values carried on <see cref="DomainEventEnvelope"/> and used
/// verbatim as the last subject token (`flexcatalog.events.&lt;eventType&gt;`).
/// Shared between the publisher (FlexCatalog.Api) and every independent
/// consumer (FlexCatalog.InventoryProjector, FlexCatalog.SearchIndexer) so
/// all sides switch on the same literals.
/// </summary>
public static class EventTypes
{
    public const string ProductCreated = "product.created";

    public const string ProductUpdated = "product.updated";

    public const string ProductDeleted = "product.deleted";

    public const string InventoryAdjusted = "inventory.adjusted";
}
