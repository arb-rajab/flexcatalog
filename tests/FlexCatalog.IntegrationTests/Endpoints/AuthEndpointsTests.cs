using System.Net;
using System.Net.Http.Json;
using FlexCatalog.Api.Dtos;
using FlexCatalog.IntegrationTests.Fixtures;

namespace FlexCatalog.IntegrationTests.Endpoints;

[Collection("Mongo collection")]
public class AuthEndpointsTests(MongoContainerFixture mongoFixture) : IntegrationTestBase(mongoFixture)
{
    [Fact]
    public async Task Login_WithSeededAdminCredentials_ReturnsTokenAndTenant()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin@acme.test", "Passw0rd!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        Assert.Equal("Admin", body.Role);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsBadRequest()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin@acme.test", "wrong-password"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownUsername_ReturnsBadRequest()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody@nowhere.test", "whatever"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        var response = await Client.GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
