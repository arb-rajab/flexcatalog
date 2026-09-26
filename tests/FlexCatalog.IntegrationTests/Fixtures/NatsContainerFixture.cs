using Testcontainers.Nats;

namespace FlexCatalog.IntegrationTests.Fixtures;

/// <summary>
/// One real nats-server, started via Testcontainers, shared across every
/// test class in the "EventStreaming collection" -- mirrors
/// <see cref="MongoContainerFixture"/>. Proves ADR 0006's design against a
/// real broker rather than an in-memory stand-in.
///
/// Requires Docker. Same sandbox caveat as MongoContainerFixture: pulling
/// the `nats` image from Docker Hub is blocked by this environment's egress
/// policy (see docs/project-memory/testing.md), so this is verified by CI.
/// </summary>
public sealed class NatsContainerFixture : IAsyncLifetime
{
    // Testcontainers.Nats's NatsBuilder already passes `--jetstream` by
    // default, so no extra configuration is needed here for ADR 0008's
    // durable consumers to work against this container.
    public NatsContainer Container { get; } = new NatsBuilder("nats:2.10-alpine").Build();

    public async Task InitializeAsync() => await Container.StartAsync();

    public async Task DisposeAsync() => await Container.DisposeAsync();
}

/// <summary>
/// Both containers together, for tests that need to prove behavior spanning
/// MongoDB and NATS (i.e. the event-streaming path, ADR 0006) rather than
/// either one alone.
/// </summary>
[CollectionDefinition("EventStreaming collection")]
public sealed class EventStreamingCollection : ICollectionFixture<MongoContainerFixture>, ICollectionFixture<NatsContainerFixture>;
