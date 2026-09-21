using FlexCatalog.Api.Domain;
using FlexCatalog.Api.Dtos;
using FlexCatalog.Api.Search;

namespace FlexCatalog.UnitTests.Search;

/// <summary>
/// Tests the pure filter-building logic (internal, exposed via
/// InternalsVisibleTo, mirroring ProductSearchServiceQueryBuildingTests'
/// rationale) without needing a live Meilisearch instance -- this is where
/// a tenant-isolation regression would actually show up.
/// </summary>
public class MeilisearchProductSearchServiceTests
{
    [Fact]
    public void BuildFilter_AlwaysIncludesTenantId()
    {
        var request = new MeilisearchSearchRequest(Query: "widget");

        var filter = MeilisearchProductSearchService.BuildFilter(request, "tenant-a");

        Assert.Equal("tenantId = \"tenant-a\"", filter);
    }

    [Fact]
    public void BuildFilter_WithNoOptionalFilters_TenantIdIsTheOnlyClause()
    {
        var request = new MeilisearchSearchRequest(Query: "widget");

        var filter = MeilisearchProductSearchService.BuildFilter(request, "tenant-a");

        Assert.DoesNotContain(" AND ", filter);
    }

    [Fact]
    public void BuildFilter_AppliesCategoryPriceAndStockFilters()
    {
        var request = new MeilisearchSearchRequest(
            Query: "widget",
            CategoryType: CategoryType.Electronics,
            InStock: true,
            MinPrice: 10m,
            MaxPrice: 100m);

        var filter = MeilisearchProductSearchService.BuildFilter(request, "tenant-a");

        Assert.Equal(
            "tenantId = \"tenant-a\" AND categoryType = \"Electronics\" AND inStock = true AND price >= 10 AND price <= 100",
            filter);
    }

    [Fact]
    public void BuildFilter_DifferentTenants_ProduceDifferentFilters()
    {
        var request = new MeilisearchSearchRequest(Query: "widget");

        var filterA = MeilisearchProductSearchService.BuildFilter(request, "tenant-a");
        var filterB = MeilisearchProductSearchService.BuildFilter(request, "tenant-b");

        Assert.NotEqual(filterA, filterB);
    }
}
