using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlexCatalog.IntegrationTests.Fixtures;

/// <summary>
/// Boots the real API (Program.cs) against a real mongod, pointed at a
/// unique per-instance database name so DataSeeder's demo tenants/users/
/// products are freshly seeded and isolated per test class.
/// </summary>
public sealed class FlexCatalogApiFactory(string mongoConnectionString) : WebApplicationFactory<Program>
{
    public string DatabaseName { get; } = $"flexcatalog_test_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mongo:ConnectionString"] = mongoConnectionString,
                ["Mongo:DatabaseName"] = DatabaseName,
                ["Jwt:Secret"] = "integration-test-signing-secret-at-least-32-bytes",
                ["Jwt:Issuer"] = "flexcatalog",
                ["Jwt:Audience"] = "flexcatalog-clients",
                ["Jwt:ExpiryMinutes"] = "60",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Hosted services (index creation + seeding) only run on Host.Run();
            // WebApplicationFactory's TestServer runs StartAsync on hosted
            // services too, so nothing extra is needed here -- kept as an
            // explicit marker for future readers.
            _ = services;
        });
    }
}
