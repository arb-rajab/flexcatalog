using System.Net.Http.Headers;
using System.Net.Http.Json;
using FlexCatalog.Api.Domain;
using FlexCatalog.Api.Dtos;
using FlexCatalog.Contracts.Eventing;
using FlexCatalog.IntegrationTests.Fixtures;
using FlexCatalog.InventoryProjector;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NATS.Client.Core;

namespace FlexCatalog.IntegrationTests.Eventing;

/// <summary>
/// The load-bearing test for ADR 0005: proves a domain event travels the
/// full real path -- HTTP write against the real API and MongoDB, over a
/// real NATS broker, into a real, independently-running consumer instance
/// (FlexCatalog.InventoryProjector's own <see cref="InventoryProjectionConsumer"/>,
/// not a stand-in), landing in its own MongoDB projection collection. No
/// step here is mocked or faked: this is the actual publisher and the
/// actual consumer, wired to actual containers.
/// </summary>
[Collection("EventStreaming collection")]
public sealed class InventoryEventStreamingTests : IAsyncLifetime
{
    private readonly MongoContainerFixture _mongoFixture;
    private readonly NatsContainerFixture _natsFixture;

    private FlexCatalogApiFactory _apiFactory = null!;
    private HttpClient _client = null!;
    private INatsConnection _consumerConnection = null!;
    private InventoryProjectionConsumer _consumer = null!;
    private IMongoCollection<ProductProjection> _projections = null!;

    public InventoryEventStreamingTests(MongoContainerFixture mongoFixture, NatsContainerFixture natsFixture)
    {
        _mongoFixture = mongoFixture;
        _natsFixture = natsFixture;
    }

    public async Task InitializeAsync()
    {
        var natsUrl = _natsFixture.Container.GetConnectionString();

        _apiFactory = new FlexCatalogApiFactory(_mongoFixture.Container.GetConnectionString(), natsUrl);
        _client = _apiFactory.CreateClient();

        var mongoClient = new MongoClient(_mongoFixture.Container.GetConnectionString());
        _projections = mongoClient.GetDatabase(_apiFactory.DatabaseName)
            .GetCollection<ProductProjection>("productInventoryProjection");

        // This is FlexCatalog.InventoryProjector's real, independent consumer
        // -- started here as its own NatsConnection/BackgroundService, not
        // sharing anything with the API host above, exactly as it would run
        // as a separate process against the same broker and database.
        _consumerConnection = new NatsConnection(NatsOpts.Default with { Url = natsUrl });
        _consumer = new InventoryProjectionConsumer(
            _consumerConnection,
            _projections,
            Options.Create(new NatsOptions()),
            NullLogger<InventoryProjectionConsumer>.Instance);
        await _consumer.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _consumer.StopAsync(CancellationToken.None);
        await _consumerConnection.DisposeAsync();
        _client.Dispose();
        await _apiFactory.DisposeAsync();
    }

    [Fact]
    public async Task ProductCreateAndInventoryAdjust_ProjectsThroughRealNatsConsumer()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin@acme.test", "Passw0rd!"));
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<LoginResponse>();

        using var client = _apiFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);

        var create = await client.PostAsJsonAsync("/api/products", new UpsertProductRequest(
            "EVT-STREAM-1", "Streaming Widget", "desc", CategoryType.Electronics, 15m, "USD", true, 10, null,
            new ElectronicsAttributes { Brand = "Acme" }));
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<ProductResponse>();

        // ProductCreated: published fire-and-forget by ProductService,
        // carried over the real NATS container, picked up by the real
        // consumer, and upserted into its own projection collection.
        var afterCreate = await WaitForProjectionAsync(created!.Id, p => p.QuantityOnHand == 10);
        Assert.Equal("EVT-STREAM-1", afterCreate.Sku);
        Assert.Equal(session.TenantId, afterCreate.TenantId);
        Assert.True(afterCreate.InStock);
        Assert.Equal(EventTypes.ProductCreated, afterCreate.LastEventType);

        var adjust = await client.PostAsJsonAsync(
            $"/api/products/{created.Id}/inventory/adjust", new AdjustInventoryRequest(-10));
        adjust.EnsureSuccessStatusCode();

        // InventoryAdjusted, driving quantity to zero: proves the consumer
        // reacts to a second, different event type through the same path,
        // and that the projection reflects the update -- not just the
        // create.
        var afterAdjust = await WaitForProjectionAsync(created.Id, p => p.QuantityOnHand == 0);
        Assert.False(afterAdjust.InStock);
        Assert.Equal(EventTypes.InventoryAdjusted, afterAdjust.LastEventType);
    }

    private async Task<ProductProjection> WaitForProjectionAsync(
        string productId, Func<ProductProjection, bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            var candidate = await _projections.Find(p => p.ProductId == productId).FirstOrDefaultAsync();
            if (candidate is not null && predicate(candidate))
            {
                return candidate;
            }

            await Task.Delay(200);
        }

        Assert.Fail($"Projection for product '{productId}' did not reach the expected state within the timeout.");
        return null!;
    }
}
