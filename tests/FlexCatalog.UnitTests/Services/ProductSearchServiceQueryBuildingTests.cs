using FlexCatalog.Api.Domain;
using FlexCatalog.Api.Dtos;
using FlexCatalog.Api.Services;
using MongoDB.Bson;

namespace FlexCatalog.UnitTests.Services;

/// <summary>
/// Tests the pure aggregation-pipeline-building logic (internal, exposed via
/// InternalsVisibleTo) without needing a live MongoDB -- these exercise the
/// exact BsonDocument shapes that get sent to Mongo, which is where filter
/// bugs and injection bugs would actually show up.
/// </summary>
public class ProductSearchServiceQueryBuildingTests
{
    [Fact]
    public void BuildMatchDocument_WithNoFilters_ReturnsEmptyDocument()
    {
        var request = new ProductSearchRequest();

        var match = ProductSearchService.BuildMatchDocument(request, usesTextSearch: false);

        Assert.True(match.ElementCount == 0);
    }

    [Fact]
    public void BuildMatchDocument_AppliesCategoryPriceAndStockFilters()
    {
        var request = new ProductSearchRequest(
            CategoryType: CategoryType.Electronics,
            MinPrice: 10m,
            MaxPrice: 100m,
            InStock: true);

        var match = ProductSearchService.BuildMatchDocument(request, usesTextSearch: false);

        Assert.Equal("Electronics", match["categoryType"].AsString);
        Assert.True(match["inStock"].AsBoolean);
        Assert.Equal(10m, match["price"]["$gte"].ToDecimal());
        Assert.Equal(100m, match["price"]["$lte"].ToDecimal());
    }

    [Fact]
    public void BuildMatchDocument_WithTextQuery_AddsTextSearchStage()
    {
        var request = new ProductSearchRequest(Query: "wireless headphones");

        var match = ProductSearchService.BuildMatchDocument(request, usesTextSearch: true);

        Assert.Equal("wireless headphones", match["$text"]["$search"].AsString);
    }

    [Fact]
    public void BuildMatchDocument_ValidAttributeFilter_BecomesInClause()
    {
        var request = new ProductSearchRequest(
            Attributes: new Dictionary<string, List<string>> { ["brand"] = ["Acme", "Pixel"] });

        var match = ProductSearchService.BuildMatchDocument(request, usesTextSearch: false);

        var inClause = match["attributes.brand"]["$in"].AsBsonArray;
        Assert.Equal(2, inClause.Count);
        Assert.Contains("Acme", inClause.Select(v => v.AsString));
    }

    [Theory]
    [InlineData("$where")]
    [InlineData("brand.$ne")]
    [InlineData("brand; DROP")]
    [InlineData("")]
    public void BuildMatchDocument_RejectsUnsafeAttributeKeys(string maliciousKey)
    {
        var request = new ProductSearchRequest(
            Attributes: new Dictionary<string, List<string>> { [maliciousKey] = ["x"] });

        var match = ProductSearchService.BuildMatchDocument(request, usesTextSearch: false);

        // The unsafe key must not appear anywhere in the resulting filter --
        // it should be silently dropped, not sanitized-and-kept.
        Assert.DoesNotContain(match.Names, name => name.Contains(maliciousKey));
    }

    [Fact]
    public void BuildMatchDocument_IgnoresAttributeEntryWithEmptyValueList()
    {
        var request = new ProductSearchRequest(
            Attributes: new Dictionary<string, List<string>> { ["brand"] = [] });

        var match = ProductSearchService.BuildMatchDocument(request, usesTextSearch: false);

        Assert.False(match.Contains("attributes.brand"));
    }

    [Fact]
    public void BuildFacetStage_AppliesPagination()
    {
        var request = new ProductSearchRequest(Page: 3, PageSize: 20);

        var facet = ProductSearchService.BuildFacetStage(request, page: 3, pageSize: 20, usesTextSearch: false);

        var resultsPipeline = facet["$facet"]["results"].AsBsonArray;
        var skipStage = resultsPipeline.Select(s => s.AsBsonDocument).First(s => s.Contains("$skip"));
        var limitStage = resultsPipeline.Select(s => s.AsBsonDocument).First(s => s.Contains("$limit"));

        Assert.Equal(40, skipStage["$skip"].ToInt32()); // (page 3 - 1) * pageSize 20
        Assert.Equal(20, limitStage["$limit"].ToInt32());
    }

    [Theory]
    [InlineData(ProductSortOrder.PriceAsc, "price", 1)]
    [InlineData(ProductSortOrder.PriceDesc, "price", -1)]
    [InlineData(ProductSortOrder.Newest, "createdAt", -1)]
    public void BuildFacetStage_SortOrderMapsToExpectedSortField(ProductSortOrder sortBy, string expectedField, int expectedDirection)
    {
        var request = new ProductSearchRequest(SortBy: sortBy);

        var facet = ProductSearchService.BuildFacetStage(request, page: 1, pageSize: 20, usesTextSearch: false);

        var resultsPipeline = facet["$facet"]["results"].AsBsonArray;
        var sortStage = resultsPipeline.Select(s => s.AsBsonDocument).First(s => s.Contains("$sort"));

        Assert.Equal(expectedDirection, sortStage["$sort"][expectedField].ToInt32());
    }
}
