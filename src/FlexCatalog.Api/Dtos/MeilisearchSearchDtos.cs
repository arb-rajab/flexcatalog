using FlexCatalog.Api.Domain;
using FlexCatalog.Contracts.Search;

namespace FlexCatalog.Api.Dtos;

/// <summary>
/// Request shape for the Meilisearch-backed endpoint (ADR 0007) -- narrower
/// than <see cref="ProductSearchRequest"/> on purpose: no per-attribute
/// filters, no facet counts. This endpoint exists for typo-tolerant,
/// relevance-ranked free text (<see cref="Query"/>); reach for
/// <c>POST /api/products/search</c> instead when the UI needs faceted
/// filter-chip navigation. See ADR 0007 for when each is the better choice.
/// </summary>
public sealed record MeilisearchSearchRequest(
    string Query,
    CategoryType? CategoryType = null,
    bool? InStock = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    int Page = 1,
    int PageSize = 20);

public sealed record MeilisearchSearchResponse(
    List<ProductSearchDocument> Items,
    long EstimatedTotalHits,
    int Page,
    int PageSize,
    int ProcessingTimeMs);
