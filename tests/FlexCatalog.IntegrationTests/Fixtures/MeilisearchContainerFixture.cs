using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace FlexCatalog.IntegrationTests.Fixtures;

/// <summary>
/// One real Meilisearch instance, started via Testcontainers, shared across
/// every test class in the "SearchIndexing collection" -- mirrors
/// <see cref="NatsContainerFixture"/>. Proves ADR 0007's design against a
/// real search engine rather than an in-memory stand-in. There is no
/// official `Testcontainers.Meilisearch` module (unlike Mongo/NATS), so
/// this wraps the generic <see cref="ContainerBuilder"/> directly.
///
/// Requires Docker. Same sandbox caveat as MongoContainerFixture/
/// NatsContainerFixture: pulling `getmeili/meilisearch` from Docker Hub is
/// blocked by this environment's egress policy (see
/// docs/project-memory/testing.md), so this is verified by CI.
/// </summary>
public sealed class MeilisearchContainerFixture : IAsyncLifetime
{
    /// <summary>
    /// A fixed, non-secret key -- this is a throwaway container that only
    /// ever exists for the lifetime of one test run, never reachable
    /// outside it.
    /// </summary>
    public const string MasterKey = "integration-test-meilisearch-master-key";

    private const int MeilisearchPort = 7700;

    public IContainer Container { get; } = new ContainerBuilder("getmeili/meilisearch:v1.15")
        .WithPortBinding(MeilisearchPort, true)
        .WithEnvironment("MEILI_MASTER_KEY", MasterKey)
        .WithEnvironment("MEILI_NO_ANALYTICS", "true")
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(request => request.ForPort(MeilisearchPort).ForPath("/health")))
        .Build();

    public string GetConnectionString() => $"http://{Container.Hostname}:{Container.GetMappedPublicPort(MeilisearchPort)}";

    public async Task InitializeAsync() => await Container.StartAsync();

    public async Task DisposeAsync() => await Container.DisposeAsync();
}

/// <summary>
/// All three containers together, for tests proving behavior spanning
/// MongoDB, NATS, and Meilisearch (the search-indexing path, ADR 0007)
/// rather than any one alone.
/// </summary>
[CollectionDefinition("SearchIndexing collection")]
public sealed class SearchIndexingCollection :
    ICollectionFixture<MongoContainerFixture>, ICollectionFixture<NatsContainerFixture>, ICollectionFixture<MeilisearchContainerFixture>;
