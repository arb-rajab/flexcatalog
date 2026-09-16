using System.Net.Http.Json;
using FlexCatalog.Api.Dtos;
using FlexCatalog.IntegrationTests.Fixtures;

namespace FlexCatalog.IntegrationTests;

[Collection("Mongo collection")]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly MongoContainerFixture _mongoFixture;

    protected IntegrationTestBase(MongoContainerFixture mongoFixture)
    {
        _mongoFixture = mongoFixture;
    }

    protected FlexCatalogApiFactory Factory { get; private set; } = null!;

    protected HttpClient Client { get; private set; } = null!;

    public Task InitializeAsync()
    {
        Factory = new FlexCatalogApiFactory(_mongoFixture.Container.GetConnectionString());
        Client = Factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
    }

    protected async Task<string> LoginAsync(string username, string password = "Passw0rd!")
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    protected HttpClient AuthenticatedClient(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
