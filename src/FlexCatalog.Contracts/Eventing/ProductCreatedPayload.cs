namespace FlexCatalog.Contracts.Eventing;

/// <summary>
/// Payload for <see cref="EventTypes.ProductCreated"/>. <see cref="Description"/>,
/// <see cref="Tags"/> and <see cref="Attributes"/> exist only for
/// FlexCatalog.SearchIndexer (ADR 0007) -- InventoryProjector's consumer
/// reads none of them, the same way it already ignores <see cref="Price"/>/
/// <see cref="Currency"/>. <see cref="Attributes"/> is the category-specific
/// attribute bag flattened to field-name -> values (e.g. { "brand": ["Acme"] }),
/// matching ProductSearchService.FacetedAttributeFields' vocabulary, since
/// Contracts cannot reference FlexCatalog.Api.Domain's polymorphic type.
/// </summary>
public sealed record ProductCreatedPayload(
    string ProductId,
    string Sku,
    string Name,
    string CategoryType,
    decimal Price,
    string Currency,
    int QuantityOnHand,
    bool InStock,
    string? Description,
    List<string> Tags,
    Dictionary<string, List<string>> Attributes);
