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
}
