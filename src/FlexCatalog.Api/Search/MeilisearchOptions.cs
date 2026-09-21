namespace FlexCatalog.Api.Search;

public sealed class MeilisearchOptions
{
    public const string SectionName = "Meilisearch";

    public string Url { get; set; } = "http://localhost:7700";

    /// <summary>
    /// Known simplification (ADR 0007): this demo uses the same master key
    /// FlexCatalog.SearchIndexer writes with, configured via the same
    /// environment variable in docker-compose.yml. A production deployment
    /// would give the API a Meilisearch search-only API key (created via
    /// Meilisearch's key-management endpoints, scoped to the `search`
    /// action) instead, so a compromised API process could never write to
    /// or reconfigure the index -- this process never needs to.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public string IndexName { get; set; } = "products";
}
