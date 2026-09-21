namespace FlexCatalog.Contracts.Eventing;

/// <summary>
/// Payload for <see cref="EventTypes.ProductDeleted"/>. Added alongside the
/// SearchIndexer work (ADR 0007): a search index that never removes deleted
/// products would keep surfacing them in results indefinitely, so deletion
/// needs to be a real event like creation, update, and inventory
/// adjustment. Deliberately minimal -- a delete only needs enough identity
/// to remove the right document, not a full snapshot.
/// </summary>
public sealed record ProductDeletedPayload(
    string ProductId,
    string Sku);
