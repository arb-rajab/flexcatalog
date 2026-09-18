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
    public void BuildStructuredMatchDocument_WithNoFilters_ReturnsEmptyDocument()
    {
        var request = new ProductSearchRequest();

        var match = ProductSearchService.BuildStructuredMatchDocument(request);

        Assert.True(match.ElementCount == 0);
    }

    [Fact]
    public void BuildStructuredMatchDocument_AppliesCategoryPriceAndStockFilters()
    {
        var request = new ProductSearchRequest(
            CategoryType: CategoryType.Electronics,
            MinPrice: 10m,
            MaxPrice: 100m,
            InStock: true);

        var match = ProductSearchService.BuildStructuredMatchDocument(request);

        Assert.Equal("Electronics", match["categoryType"].AsString);
        Assert.True(match["inStock"].AsBoolean);
        Assert.Equal(10m, match["price"]["$gte"].ToDecimal());
        Assert.Equal(100m, match["price"]["$lte"].ToDecimal());
    }

    [Fact]
    public void BuildStructuredMatchDocument_NeverIncludesText()
    {
        // $text lives in its own top-level $match (BuildTextMatchDocument)
        // because MongoDB doesn't allow $text inside a $facet sub-pipeline,
        // which is where every structured filter now lives.
        var request = new ProductSearchRequest(Query: "wireless headphones", CategoryType: CategoryType.Electronics);

        var match = ProductSearchService.BuildStructuredMatchDocument(request);

        Assert.False(match.Contains("$text"));
        Assert.Equal("Electronics", match["categoryType"].AsString);
    }

    [Fact]
    public void BuildTextMatchDocument_WrapsQueryAsTextSearch()
    {
        var match = ProductSearchService.BuildTextMatchDocument("wireless headphones");

        Assert.Equal("wireless headphones", match["$text"]["$search"].AsString);
    }

    [Fact]
    public void BuildStructuredMatchDocument_ExcludeCategory_OmitsCategoryButKeepsOtherFilters()
    {
        var request = new ProductSearchRequest(CategoryType: CategoryType.Electronics, InStock: true);

        var match = ProductSearchService.BuildStructuredMatchDocument(request, excludeCategory: true);

        Assert.False(match.Contains("categoryType"));
        Assert.True(match["inStock"].AsBoolean);
    }

    [Fact]
    public void BuildStructuredMatchDocument_ExcludeAttributeKey_OmitsOnlyThatAttributeFilter()
    {
        var request = new ProductSearchRequest(
            Attributes: new Dictionary<string, List<string>>
            {
                ["brand"] = ["Acme"],
                ["colors"] = ["Red"],
            });

        var match = ProductSearchService.BuildStructuredMatchDocument(request, excludeAttributeKey: "brand");

        Assert.False(match.Contains("attributes.brand"));
        Assert.True(match.Contains("attributes.colors"));
    }

    [Fact]
    public void BuildStructuredMatchDocument_ValidAttributeFilter_BecomesInClause()
    {
        var request = new ProductSearchRequest(
            Attributes: new Dictionary<string, List<string>> { ["brand"] = ["Acme", "Pixel"] });

        var match = ProductSearchService.BuildStructuredMatchDocument(request);

        var inClause = match["attributes.brand"]["$in"].AsBsonArray;
        Assert.Equal(2, inClause.Count);
        Assert.Contains("Acme", inClause.Select(v => v.AsString));
    }

    [Theory]
    [InlineData("$where")]
    [InlineData("brand.$ne")]
    [InlineData("brand; DROP")]
    [InlineData("")]
    public void BuildStructuredMatchDocument_RejectsUnsafeAttributeKeys(string maliciousKey)
    {
        var request = new ProductSearchRequest(
            Attributes: new Dictionary<string, List<string>> { [maliciousKey] = ["x"] });

        var match = ProductSearchService.BuildStructuredMatchDocument(request);

        // The unsafe key must not appear anywhere in the resulting filter --
        // it should be silently dropped, not sanitized-and-kept.
        Assert.DoesNotContain(match.Names, name => name.Contains(maliciousKey));
    }

    [Fact]
    public void BuildStructuredMatchDocument_IgnoresAttributeEntryWithEmptyValueList()
    {
        var request = new ProductSearchRequest(
            Attributes: new Dictionary<string, List<string>> { ["brand"] = [] });

        var match = ProductSearchService.BuildStructuredMatchDocument(request);

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

    [Fact]
    public void BuildFacetStage_CategoryFacetBranch_ExcludesCategoryFilterButKeepsOtherFilters()
    {
        var request = new ProductSearchRequest(CategoryType: CategoryType.Electronics, InStock: true);

        var facet = ProductSearchService.BuildFacetStage(request, page: 1, pageSize: 20, usesTextSearch: false);

        var categoryFacetPipeline = facet["$facet"]["categoryFacet"].AsBsonArray;
        var matchStage = categoryFacetPipeline.Select(s => s.AsBsonDocument).First(s => s.Contains("$match"));

        // Independent-branch faceting (ADR 0004): a caller filtering to
        // Electronics must still see counts for the other categories.
        Assert.False(matchStage["$match"].AsBsonDocument.Contains("categoryType"));
        Assert.True(matchStage["$match"]["inStock"].AsBoolean);
    }

    [Fact]
    public void BuildFacetStage_AttributeFacetBranch_ExcludesOwnFilterButKeepsOtherAttributeFilters()
    {
        var request = new ProductSearchRequest(
            Attributes: new Dictionary<string, List<string>>
            {
                ["brand"] = ["Acme"],
                ["colors"] = ["Red"],
            });

        var facet = ProductSearchService.BuildFacetStage(request, page: 1, pageSize: 20, usesTextSearch: false);

        var brandFacetPipeline = facet["$facet"]["attr_brand"].AsBsonArray;
        var matchStage = brandFacetPipeline.Select(s => s.AsBsonDocument).First(s => s.Contains("$match"))["$match"].AsBsonDocument;

        // The brand facet's own filter must not narrow its own counts --
        // selecting "Acme" should still let the UI show Sony/LG as options
        // (only the $exists guard remains for "brand") -- but a *different*
        // attribute's filter (colors) still applies in full.
        Assert.False(matchStage["attributes.brand"].AsBsonDocument.Contains("$in"));
        Assert.True(matchStage["attributes.brand"]["$exists"].AsBoolean);
        Assert.Single(matchStage["attributes.colors"]["$in"].AsBsonArray);
    }

    [Fact]
    public void BuildFacetStage_PriceRangeBranch_StaysNetOfFullFilterIncludingPrice()
    {
        // Deliberate exception to independent-branch faceting -- see the
        // ProductSearchService class doc comment and ADR 0004.
        var request = new ProductSearchRequest(MinPrice: 10m, MaxPrice: 100m, CategoryType: CategoryType.Electronics);

        var facet = ProductSearchService.BuildFacetStage(request, page: 1, pageSize: 20, usesTextSearch: false);

        var priceRangePipeline = facet["$facet"]["priceRange"].AsBsonArray;
        var matchStage = priceRangePipeline.Select(s => s.AsBsonDocument).First(s => s.Contains("$match"))["$match"].AsBsonDocument;

        Assert.Equal(10m, matchStage["price"]["$gte"].ToDecimal());
        Assert.Equal("Electronics", matchStage["categoryType"].AsString);
    }

    [Fact]
    public void BuildFacetStage_ResultsAndTotalCountBranches_ApplyTheFullFilter()
    {
        var request = new ProductSearchRequest(CategoryType: CategoryType.Electronics, InStock: true);

        var facet = ProductSearchService.BuildFacetStage(request, page: 1, pageSize: 20, usesTextSearch: false);

        var resultsMatch = facet["$facet"]["results"].AsBsonArray
            .Select(s => s.AsBsonDocument).First(s => s.Contains("$match"))["$match"].AsBsonDocument;
        var totalCountMatch = facet["$facet"]["totalCount"].AsBsonArray
            .Select(s => s.AsBsonDocument).First(s => s.Contains("$match"))["$match"].AsBsonDocument;

        Assert.Equal("Electronics", resultsMatch["categoryType"].AsString);
        Assert.Equal("Electronics", totalCountMatch["categoryType"].AsString);
    }
}
