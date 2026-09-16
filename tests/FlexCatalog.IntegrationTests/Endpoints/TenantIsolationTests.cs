using System.Net;
using System.Net.Http.Json;
using FlexCatalog.Api.Domain;
using FlexCatalog.Api.Dtos;
using FlexCatalog.IntegrationTests.Fixtures;

namespace FlexCatalog.IntegrationTests.Endpoints;

/// <summary>
/// The whole point of the multi-tenant isolation ADR: prove, over real HTTP
/// against a real mongod, that one tenant's admin can never read, search, or
/// mutate another tenant's data -- not just that the repository code
/// "should" prevent it.
/// </summary>
[Collection("Mongo collection")]
public class TenantIsolationTests(MongoContainerFixture mongoFixture) : IntegrationTestBase(mongoFixture)
{
    [Fact]
    public async Task ProductCreatedByOneTenant_IsNotVisibleByIdToAnotherTenant()
    {
        var acmeToken = await LoginAsync("admin@acme.test");
        using var acmeClient = AuthenticatedClient(acmeToken);

        var createResponse = await acmeClient.PostAsJsonAsync("/api/products", new UpsertProductRequest(
            "ISO-TEST-1", "Isolation Test Widget", null, CategoryType.Electronics, 50m, "USD", true, 10, null,
            new ElectronicsAttributes { Brand = "Acme" }));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponse>();

        var urbanToken = await LoginAsync("admin@urbanthread.test");
        using var urbanClient = AuthenticatedClient(urbanToken);

        var getResponse = await urbanClient.GetAsync($"/api/products/{created!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Search_NeverReturnsAnotherTenantsProducts()
    {
        var acmeToken = await LoginAsync("admin@acme.test");
        using var acmeClient = AuthenticatedClient(acmeToken);
        var createResponse = await acmeClient.PostAsJsonAsync("/api/products", new UpsertProductRequest(
            "ISO-TEST-2", "Only Acme Should See This", null, CategoryType.Electronics, 75m, "USD", true, 5, null,
            new ElectronicsAttributes { Brand = "OnlyAcme" }));
        createResponse.EnsureSuccessStatusCode();

        var urbanToken = await LoginAsync("admin@urbanthread.test");
        using var urbanClient = AuthenticatedClient(urbanToken);
        var searchResponse = await urbanClient.PostAsJsonAsync("/api/products/search", new ProductSearchRequest());
        searchResponse.EnsureSuccessStatusCode();
        var results = await searchResponse.Content.ReadFromJsonAsync<ProductSearchResponse>();

        Assert.DoesNotContain(results!.Items, p => p.Sku == "ISO-TEST-2");
    }

    [Fact]
    public async Task DeleteByOneTenant_CannotDeleteAnotherTenantsProduct()
    {
        var acmeToken = await LoginAsync("admin@acme.test");
        using var acmeClient = AuthenticatedClient(acmeToken);
        var createResponse = await acmeClient.PostAsJsonAsync("/api/products", new UpsertProductRequest(
            "ISO-TEST-3", "Protected Product", null, CategoryType.Books, 15m, "USD", true, 5, null,
            new BookAttributes { Author = "Someone" }));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponse>();

        var urbanToken = await LoginAsync("admin@urbanthread.test");
        using var urbanClient = AuthenticatedClient(urbanToken);
        var deleteResponse = await urbanClient.DeleteAsync($"/api/products/{created!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);

        // Confirm it's untouched from the owning tenant's perspective.
        var getResponse = await acmeClient.GetAsync($"/api/products/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }
}
