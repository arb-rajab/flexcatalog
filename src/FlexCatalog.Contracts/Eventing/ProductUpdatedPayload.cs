namespace FlexCatalog.Contracts.Eventing;

/// <summary>
/// Payload for <see cref="EventTypes.ProductUpdated"/>. Added alongside the
/// SearchIndexer work (ADR 0007) because a full-text search index needs to
/// react to edits, not just creation and inventory changes -- the write
/// path this rides on (<c>ProductService.UpdateAsync</c>) already existed,
/// it just never published anything. Carries the same fields as the
/// enriched <see cref="ProductCreatedPayload"/> (a full current snapshot,
/// not a diff) so a consumer can re-index in place without a follow-up
/// read.
/// </summary>
public sealed record ProductUpdatedPayload(
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
