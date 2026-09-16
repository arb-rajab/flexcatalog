using System.Net;
using System.Net.Http.Json;
using FlexCatalog.Api.Domain;
using FlexCatalog.Api.Dtos;
using FlexCatalog.IntegrationTests.Fixtures;

namespace FlexCatalog.IntegrationTests.Endpoints;

[Collection("Mongo collection")]
public class ProductCrudTests(MongoContainerFixture mongoFixture) : IntegrationTestBase(mongoFixture)
{
    [Fact]
    public async Task Admin_CanCreateUpdateAndDeleteProduct()
    {
        var token = await LoginAsync("admin@acme.test");
        using var client = AuthenticatedClient(token);

        var create = await client.PostAsJsonAsync("/api/products", new UpsertProductRequest(
            "CRUD-1", "Test Product", "desc", CategoryType.Electronics, 20m, "USD", true, 3, null,
            new ElectronicsAttributes { Brand = "Acme" }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ProductResponse>();

        var update = await client.PutAsJsonAsync($"/api/products/{created!.Id}", new UpsertProductRequest(
            "CRUD-1", "Updated Name", "desc", CategoryType.Electronics, 25m, "USD", true, 3, null,
            new ElectronicsAttributes { Brand = "Acme" }));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.Equal("Updated Name", updated!.Name);
        Assert.Equal(25m, updated.Price);

        var delete = await client.DeleteAsync($"/api/products/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var getAfterDelete = await client.GetAsync($"/api/products/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotCreateProduct()
    {
        var token = await LoginAsync("viewer@acme.test");
        using var client = AuthenticatedClient(token);

        var response = await client.PostAsJsonAsync("/api/products", new UpsertProductRequest(
            "CRUD-2", "Should Fail", null, CategoryType.Electronics, 10m, "USD", true, 1, null,
            new ElectronicsAttributes { Brand = "Acme" }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CanReadAndSearchProducts()
    {
        var token = await LoginAsync("viewer@acme.test");
        using var client = AuthenticatedClient(token);

        var response = await client.PostAsJsonAsync("/api/products/search", new ProductSearchRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_WithDuplicateSku_ReturnsConflict()
    {
        var token = await LoginAsync("admin@acme.test");
        using var client = AuthenticatedClient(token);

        var request = new UpsertProductRequest(
            "CRUD-DUP", "First", null, CategoryType.Electronics, 10m, "USD", true, 1, null,
            new ElectronicsAttributes { Brand = "Acme" });

        var first = await client.PostAsJsonAsync("/api/products", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/products", request);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task AdjustInventory_BelowZero_ReturnsBadRequest()
    {
        var token = await LoginAsync("admin@acme.test");
        using var client = AuthenticatedClient(token);

        var create = await client.PostAsJsonAsync("/api/products", new UpsertProductRequest(
            "CRUD-INV", "Inventory Test", null, CategoryType.Books, 5m, "USD", true, 2, null,
            new BookAttributes { Author = "A" }));
        var created = await create.Content.ReadFromJsonAsync<ProductResponse>();

        var response = await client.PostAsJsonAsync($"/api/products/{created!.Id}/inventory/adjust", new AdjustInventoryRequest(-10));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
