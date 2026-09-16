using Testcontainers.MongoDb;

namespace FlexCatalog.IntegrationTests.Fixtures;

/// <summary>
/// One real mongod, started via Testcontainers, shared across every test
/// class in the "Mongo collection". Each test class still gets its own
/// database name (see FlexCatalogApiFactory) so tests across classes never
/// see each other's data, while avoiding the cost of a fresh container per
/// class.
///
/// Requires Docker to be available to the test runner. In this sandbox,
/// pulling mongo:7.0 from Docker Hub is blocked by the environment's egress
/// policy (see docs/project-memory/testing.md), so these tests are verified
/// by CI (GitHub Actions), not locally.
/// </summary>
public sealed class MongoContainerFixture : IAsyncLifetime
{
    public MongoDbContainer Container { get; } = new MongoDbBuilder("mongo:7.0").Build();

    public async Task InitializeAsync() => await Container.StartAsync();

    public async Task DisposeAsync() => await Container.DisposeAsync();
}

[CollectionDefinition("Mongo collection")]
public sealed class MongoCollection : ICollectionFixture<MongoContainerFixture>;
