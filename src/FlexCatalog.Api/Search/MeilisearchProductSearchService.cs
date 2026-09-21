using System.Globalization;
using FlexCatalog.Api.Auth;
using FlexCatalog.Api.Dtos;
using FlexCatalog.Contracts.Search;
using Meilisearch;
using Microsoft.Extensions.Options;

namespace FlexCatalog.Api.Search;

public interface IMeilisearchProductSearchService
{
    Task<MeilisearchSearchResponse> SearchAsync(MeilisearchSearchRequest request, CancellationToken ct = default);
}

/// <summary>
/// The read-only counterpart to FlexCatalog.SearchIndexer's write path
/// (ADR 0007): queries the same shared Meilisearch index that consumer
/// alone writes to, for full-text/typo-tolerant search -- additive
/// alongside <see cref="ProductSearchService"/>'s $facet endpoint, not a
/// replacement (see ADR 0004 vs ADR 0007 for when each is the better fit).
///
/// Tenant isolation mirrors ADR 0001's structural approach for MongoDB:
/// <see cref="BuildFilter"/> unconditionally ANDs a `tenantId = "..."`
/// clause taken only from <see cref="ITenantContext"/> (JWT-derived, never
/// client-supplied) onto every query, the same way
/// ProductRepository.AggregateTenantScopedAsync forcibly prepends a
/// tenant $match stage -- there is no code path here that can query
/// Meilisearch without it.
/// </summary>
public sealed class MeilisearchProductSearchService(
    MeilisearchClient client,
    IOptions<MeilisearchOptions> options,
    ITenantContext tenantContext) : IMeilisearchProductSearchService
{
    public async Task<MeilisearchSearchResponse> SearchAsync(MeilisearchSearchRequest request, CancellationToken ct = default)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var index = client.Index(options.Value.IndexName);
        var searchQuery = new SearchQuery
        {
            Filter = BuildFilter(request, tenantContext.TenantId),
            Limit = pageSize,
            Offset = (page - 1) * pageSize,
        };

        var result = await index.SearchAsync<ProductSearchDocument>(request.Query, searchQuery, ct);

        // SearchAsync's declared return type is the narrower ISearchable<T>
        // (shared with facet/hybrid search variants), but an offset/limit
        // query like this one always deserializes to the concrete
        // SearchResult<T>, which is the only variant that carries
        // EstimatedTotalHits -- falls back to the hit count on this page in
        // the (currently unreachable in practice) case it doesn't.
        var estimatedTotalHits = result is SearchResult<ProductSearchDocument> { } concrete
            ? concrete.EstimatedTotalHits
            : result.Hits.Count;

        return new MeilisearchSearchResponse(
            [.. result.Hits],
            estimatedTotalHits,
            page,
            pageSize,
            result.ProcessingTimeMs);
    }

    /// <summary>
    /// Builds the Meilisearch filter expression. `tenantId` is never
    /// interpolated from anything a caller supplied -- it comes from
    /// <see cref="ITenantContext"/>, i.e. the validated JWT (ADR 0001) --
    /// so there is no filter-injection risk from that value the way there
    /// would be from a client-supplied string. `categoryType` likewise
    /// comes from a real C# enum, not free text. Exposed `internal` (like
    /// ProductSearchService.BuildStructuredMatchDocument) so the filter
    /// shape is unit-testable without a live Meilisearch instance.
    /// </summary>
    internal static string BuildFilter(MeilisearchSearchRequest request, string tenantId)
    {
        var filters = new List<string> { $"tenantId = \"{tenantId}\"" };

        if (request.CategoryType.HasValue)
        {
            filters.Add($"categoryType = \"{request.CategoryType.Value}\"");
        }

        if (request.InStock.HasValue)
        {
            filters.Add($"inStock = {(request.InStock.Value ? "true" : "false")}");
        }

        if (request.MinPrice.HasValue)
        {
            filters.Add($"price >= {request.MinPrice.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (request.MaxPrice.HasValue)
        {
            filters.Add($"price <= {request.MaxPrice.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return string.Join(" AND ", filters);
    }
}
