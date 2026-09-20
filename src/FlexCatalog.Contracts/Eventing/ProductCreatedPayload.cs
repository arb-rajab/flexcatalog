namespace FlexCatalog.Contracts.Eventing;

/// <summary>Payload for <see cref="EventTypes.ProductCreated"/>.</summary>
public sealed record ProductCreatedPayload(
    string ProductId,
    string Sku,
    string Name,
    string CategoryType,
    decimal Price,
    string Currency,
    int QuantityOnHand,
    bool InStock);
