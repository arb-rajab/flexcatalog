using System.Net.Http.Json;
using FlexCatalog.Api.Domain;
using FlexCatalog.Api.Dtos;
using FlexCatalog.IntegrationTests.Fixtures;

namespace FlexCatalog.IntegrationTests.Endpoints;

[Collection("Mongo collection")]
public class SearchFacetsTests(MongoContainerFixture mongoFixture) : IntegrationTestBase(mongoFixture)
{
    [Fact]
    public async Task Search_FiltersByCategoryAndPriceRange()
    {
        var token = await LoginAsync("admin@acme.test");
        using var client = AuthenticatedClient(token);

        var response = await client.PostAsJsonAsync("/api/products/search", new ProductSearchRequest(
            CategoryType: CategoryType.Electronics,
            MinPrice: 0m,
            MaxPrice: 300m));
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ProductSearchResponse>();

        Assert.NotNull(result);
        Assert.All(result!.Items, p => Assert.Equal(CategoryType.Electronics, p.CategoryType));
        Assert.All(result.Items, p => Assert.InRange(p.Price, 0m, 300m));
    }

    [Fact]
    public async Task Search_ByAttributeFilter_ReturnsOnlyMatchingBrand()
    {
        var token = await LoginAsync("admin@acme.test");
        using var client = AuthenticatedClient(token);

        var response = await client.PostAsJsonAsync("/api/products/search", new ProductSearchRequest(
            Attributes: new Dictionary<string, List<string>> { ["brand"] = ["Acme"] }));
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ProductSearchResponse>();

        Assert.NotEmpty(result!.Items);
        Assert.All(result.Items, p => Assert.Equal("Acme", ((ElectronicsAttributes)p.Attributes).Brand));
    }

    [Fact]
    public async Task Search_ReturnsCategoryFacetCounts()
    {
        var token = await LoginAsync("admin@urbanthread.test");
        using var client = AuthenticatedClient(token);

        var response = await client.PostAsJsonAsync("/api/products/search", new ProductSearchRequest());
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ProductSearchResponse>();

        Assert.NotEmpty(result!.Facets.Categories);
        Assert.Equal(result.TotalCount, result.Facets.Categories.Sum(c => c.Count));
    }

    [Fact]
    public async Task Search_InStockFilter_ExcludesOutOfStockProducts()
    {
        var token = await LoginAsync("admin@acme.test");
        using var client = AuthenticatedClient(token);

        var response = await client.PostAsJsonAsync("/api/products/search", new ProductSearchRequest(InStock: true));
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ProductSearchResponse>();

        Assert.All(result!.Items, p => Assert.True(p.InStock));
    }

    [Fact]
    public async Task Search_AttributeFacet_IsIndependentOfItsOwnFilter()
    {
        // Independent-branch faceting (ADR 0004): filtering to brand=Acme
        // must narrow the *results*, but the brand facet itself should
        // still list the other available brand (Pixel, from the seeded
        // "Pixel Vision Camera") so a UI can offer switching brands.
        var token = await LoginAsync("admin@acme.test");
        using var client = AuthenticatedClient(token);

        var response = await client.PostAsJsonAsync("/api/products/search", new ProductSearchRequest(
            Attributes: new Dictionary<string, List<string>> { ["brand"] = ["Acme"] }));
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ProductSearchResponse>();

        Assert.All(result!.Items, p => Assert.Equal("Acme", ((ElectronicsAttributes)p.Attributes).Brand));

        var brandFacet = result.Facets.Attributes["brand"];
        Assert.Contains(brandFacet, f => f.Value == "Acme");
        Assert.Contains(brandFacet, f => f.Value == "Pixel");
    }

    [Fact]
    public async Task Search_CategoryFacet_IsIndependentOfCategoryFilter()
    {
        // UrbanThread has both Apparel (2 products) and Books (1 product)
        // seeded. Filtering to Apparel must narrow the results, but the
        // category facet should still show Books as an available option.
        var token = await LoginAsync("admin@urbanthread.test");
        using var client = AuthenticatedClient(token);

        var response = await client.PostAsJsonAsync("/api/products/search", new ProductSearchRequest(
            CategoryType: CategoryType.Apparel));
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ProductSearchResponse>();

        Assert.All(result!.Items, p => Assert.Equal(CategoryType.Apparel, p.CategoryType));
        Assert.Contains(result.Facets.Categories, c => c.Value == "Apparel");
        Assert.Contains(result.Facets.Categories, c => c.Value == "Books");
    }
}
