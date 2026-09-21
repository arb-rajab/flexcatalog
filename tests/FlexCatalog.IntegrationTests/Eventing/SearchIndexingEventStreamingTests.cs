using System.Net.Http.Headers;
using System.Net.Http.Json;
using FlexCatalog.Api.Domain;
using FlexCatalog.Api.Dtos;
using FlexCatalog.Contracts.Search;
using FlexCatalog.IntegrationTests.Fixtures;
using Meilisearch;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;

namespace FlexCatalog.IntegrationTests.Eventing;

/// <summary>
/// The load-bearing test for ADR 0007: proves a product write travels the
/// full real path -- HTTP write against the real API and MongoDB, over a
/// real NATS broker, into a real, independently-running
/// FlexCatalog.SearchIndexer.SearchIndexingConsumer (not a stand-in),
/// landing in a real Meilisearch index and becoming genuinely *searchable*
/// (typo-tolerant, not just present). Also proves the full product
/// lifecycle an index needs to stay correct: create, update, inventory
/// adjustment, and delete. Nothing here is mocked: real API, real MongoDB,
/// real NATS broker, real consumer code, real Meilisearch instance.
/// </summary>
[Collection("SearchIndexing collection")]
public sealed class SearchIndexingEventStreamingTests : IAsyncLifetime
{
    private const string IndexName = "products-integration-test";

    private readonly MongoContainerFixture _mongoFixture;
    private readonly NatsContainerFixture _natsFixture;
    private readonly MeilisearchContainerFixture _meilisearchFixture;

    private FlexCatalogApiFactory _apiFactory = null!;
    private HttpClient _client = null!;
    private INatsConnection _consumerConnection = null!;
    private MeilisearchClient _meilisearchClient = null!;
    private FlexCatalog.SearchIndexer.SearchIndexingConsumer _consumer = null!;

    public SearchIndexingEventStreamingTests(
        MongoContainerFixture mongoFixture, NatsContainerFixture natsFixture, MeilisearchContainerFixture meilisearchFixture)
    {
        _mongoFixture = mongoFixture;
        _natsFixture = natsFixture;
        _meilisearchFixture = meilisearchFixture;
    }

    public async Task InitializeAsync()
    {
        var natsUrl = _natsFixture.Container.GetConnectionString();

        _apiFactory = new FlexCatalogApiFactory(_mongoFixture.Container.GetConnectionString(), natsUrl);
        _client = _apiFactory.CreateClient();

        _meilisearchClient = new MeilisearchClient(_meilisearchFixture.GetConnectionString(), MeilisearchContainerFixture.MasterKey);

        // This is FlexCatalog.SearchIndexer's real, independent consumer --
        // started here as its own NatsConnection/BackgroundService, not
        // sharing anything with the API host above, exactly as it would run
        // as a separate process against the same broker and search engine.
        _consumerConnection = new NatsConnection(NatsOpts.Default with { Url = natsUrl });
        _consumer = new FlexCatalog.SearchIndexer.SearchIndexingConsumer(
            _consumerConnection,
            _meilisearchClient,
            Options.Create(new FlexCatalog.SearchIndexer.MeilisearchOptions { IndexName = IndexName }),
            Options.Create(new FlexCatalog.SearchIndexer.NatsOptions()),
            NullLogger<FlexCatalog.SearchIndexer.SearchIndexingConsumer>.Instance);
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
    public async Task ProductLifecycle_ProjectsThroughRealSearchIndexingConsumer()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin@acme.test", "Passw0rd!"));
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<LoginResponse>();

        using var client = _apiFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);

        var create = await client.PostAsJsonAsync("/api/products", new UpsertProductRequest(
            "SEARCH-EVT-1", "Streaming Headphones", "Noise-cancelling wireless headphones", CategoryType.Electronics,
            129.99m, "USD", true, 8, ["audio"], new ElectronicsAttributes { Brand = "Acme" }));
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<ProductResponse>();

        var documentId = ProductSearchDocument.DocumentId(session.TenantId, created!.Id);

        // ProductCreated: published fire-and-forget by ProductService,
        // carried over the real NATS container, picked up by the real
        // consumer, and added to the real Meilisearch index.
        var afterCreate = await WaitForDocumentAsync(documentId, d => d.QuantityOnHand == 8);
        Assert.Equal("SEARCH-EVT-1", afterCreate.Sku);
        Assert.Equal(session.TenantId, afterCreate.TenantId);
        Assert.Equal("Acme", afterCreate.Brand);
        Assert.True(afterCreate.InStock);

        // Proves the document is genuinely *searchable* -- typo-tolerant
        // free text against Meilisearch itself, not a raw GetDocumentAsync
        // lookup by primary key.
        await WaitForSearchHitAsync(session.TenantId, "Streming Hedphones", documentId);

        var update = await client.PutAsJsonAsync($"/api/products/{created.Id}", new UpsertProductRequest(
            "SEARCH-EVT-1", "Updated Studio Headphones", "Still noise-cancelling", CategoryType.Electronics,
            149.99m, "USD", true, 8, ["audio", "studio"], new ElectronicsAttributes { Brand = "Acme" }));
        update.EnsureSuccessStatusCode();

        // ProductUpdated (new in ADR 0007): proves the consumer reacts to
        // an edit, not just the initial create.
        await WaitForDocumentAsync(documentId, d => d.Name == "Updated Studio Headphones" && d.Price == 149.99m);

        var adjust = await client.PostAsJsonAsync($"/api/products/{created.Id}/inventory/adjust", new AdjustInventoryRequest(-8));
        adjust.EnsureSuccessStatusCode();

        // InventoryAdjusted, driving quantity to zero: proves the
        // partial-document stock update path.
        var afterAdjust = await WaitForDocumentAsync(documentId, d => d.QuantityOnHand == 0);
        Assert.False(afterAdjust.InStock);

        var delete = await client.DeleteAsync($"/api/products/{created.Id}");
        delete.EnsureSuccessStatusCode();

        // ProductDeleted (new in ADR 0007): proves a deleted product stops
        // being searchable, not just stale.
        await WaitForDocumentRemovedAsync(documentId);
    }

    private async Task<ProductSearchDocument> WaitForDocumentAsync(
        string documentId, Func<ProductSearchDocument, bool> predicate, TimeSpan? timeout = null)
    {
        var index = _meilisearchClient.Index(IndexName);
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var candidate = await index.GetDocumentAsync<ProductSearchDocument>(documentId);
                if (predicate(candidate))
                {
                    return candidate;
                }
            }
            catch (MeilisearchApiError)
            {
                // Not indexed yet -- keep polling.
            }

            await Task.Delay(200);
        }

        Assert.Fail($"Meilisearch document '{documentId}' did not reach the expected state within the timeout.");
        return null!;
    }

    private async Task WaitForDocumentRemovedAsync(string documentId, TimeSpan? timeout = null)
    {
        var index = _meilisearchClient.Index(IndexName);
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await index.GetDocumentAsync<ProductSearchDocument>(documentId);
            }
            catch (MeilisearchApiError)
            {
                return;
            }

            await Task.Delay(200);
        }

        Assert.Fail($"Meilisearch document '{documentId}' was not removed within the timeout.");
    }

    private async Task WaitForSearchHitAsync(string tenantId, string query, string documentId, TimeSpan? timeout = null)
    {
        var index = _meilisearchClient.Index(IndexName);
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline)
        {
            var result = await index.SearchAsync<ProductSearchDocument>(
                query, new SearchQuery { Filter = $"tenantId = \"{tenantId}\"" });
            if (result.Hits.Any(hit => hit.Id == documentId))
            {
                return;
            }

            await Task.Delay(200);
        }

        Assert.Fail($"Meilisearch search for '{query}' did not surface document '{documentId}' within the timeout.");
    }
}
