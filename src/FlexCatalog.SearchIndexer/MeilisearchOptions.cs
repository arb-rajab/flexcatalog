namespace FlexCatalog.SearchIndexer;

public sealed class MeilisearchOptions
{
    public const string SectionName = "Meilisearch";

    public string Url { get; set; } = "http://localhost:7700";

    /// <summary>
    /// The instance's master key -- this process is the only writer to the
    /// index (ADR 0007), so it needs full admin rights (create the index,
    /// change its settings, add/update/delete documents), unlike
    /// FlexCatalog.Api's own Search.MeilisearchOptions, which only ever
    /// needs a search-scoped key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public string IndexName { get; set; } = "products";
}
