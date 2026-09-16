using FlexCatalog.Api.Domain;

namespace FlexCatalog.Api.Dtos;

public sealed record ProductSearchRequest(
    CategoryType? CategoryType = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    bool? InStock = null,
    string? Query = null,
    /// <summary>
    /// Category-specific attribute filters, e.g. { "brand": ["Sony","LG"], "size": ["M"] }.
    /// Field names are matched case-insensitively against the attribute
    /// bag's JSON property names (brand, sizes, author, ...).
    /// </summary>
    Dictionary<string, List<string>>? Attributes = null,
    int Page = 1,
    int PageSize = 20,
    ProductSortOrder SortBy = ProductSortOrder.Relevance);

public enum ProductSortOrder
{
    Relevance,
    PriceAsc,
    PriceDesc,
    Newest
}

public sealed record FacetCount(string Value, int Count);

public sealed record PriceRangeFacet(decimal? Min, decimal? Max);

public sealed record SearchFacets(
    List<FacetCount> Categories,
    Dictionary<string, List<FacetCount>> Attributes,
    PriceRangeFacet PriceRange);

public sealed record ProductSearchResponse(
    List<ProductResponse> Items,
    long TotalCount,
    int Page,
    int PageSize,
    SearchFacets Facets);
