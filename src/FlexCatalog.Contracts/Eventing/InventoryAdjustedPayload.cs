namespace FlexCatalog.Contracts.Eventing;

/// <summary>Payload for <see cref="EventTypes.InventoryAdjusted"/>.</summary>
public sealed record InventoryAdjustedPayload(
    string ProductId,
    string Sku,
    int Delta,
    int PreviousQuantity,
    int NewQuantity,
    bool InStock);
