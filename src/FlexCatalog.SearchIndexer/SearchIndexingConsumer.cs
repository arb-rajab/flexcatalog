using System.Text.Json;
using FlexCatalog.Contracts.Eventing;
using FlexCatalog.Contracts.Search;
using Meilisearch;
using Microsoft.Extensions.Options;
using NATS.Client.Core;

namespace FlexCatalog.SearchIndexer;

/// <summary>
/// Sibling of FlexCatalog.InventoryProjector.InventoryProjectionConsumer
/// (ADR 0007): an independent consumer of the same
/// `flexcatalog.events.&gt;` subject hierarchy (ADR 0006), except instead of
/// building a MongoDB read-model it builds a Meilisearch index -- the same
/// "independent process reacting to a fact that already happened" shape,
/// a different downstream store. Never queries FlexCatalog.Api's own
/// `products` collection; everything it writes to Meilisearch comes from
/// the event payloads alone.
/// </summary>
public sealed class SearchIndexingConsumer(
    INatsConnection connection,
    MeilisearchClient meilisearch,
    IOptions<MeilisearchOptions> meilisearchOptions,
    IOptions<NatsOptions> natsOptions,
    ILogger<SearchIndexingConsumer> logger) : BackgroundService
{
    private Meilisearch.Index Index => meilisearch.Index(meilisearchOptions.Value.IndexName);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureIndexConfiguredAsync(stoppingToken);

        var subject = $"{natsOptions.Value.SubjectPrefix}.>";

        // Same retry/backoff discipline as InventoryProjectionConsumer (see
        // its comment for the full rationale): SubscribeAsync connects
        // eagerly, so a NATS outage at startup must not take this
        // BackgroundService's host down.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                logger.LogInformation("Subscribing to {Subject}", subject);
                await foreach (var msg in connection.SubscribeAsync<string>(subject, cancellationToken: stoppingToken))
                {
                    try
                    {
                        await HandleAsync(msg.Data, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to process message on subject {Subject}", msg.Subject);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Subscription to {Subject} failed; retrying in 5s.", subject);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    /// <summary>
    /// Creates the index and configures its filterable/searchable/sortable
    /// attributes if they don't already exist. Idempotent (safe to call on
    /// every process start) and retried with the same fixed backoff as the
    /// subscription loop above -- Meilisearch being briefly unreachable at
    /// startup (e.g. containers starting in parallel in docker-compose)
    /// must not crash this host either.
    /// </summary>
    internal async Task EnsureIndexConfiguredAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var createTask = await meilisearch.CreateIndexAsync(meilisearchOptions.Value.IndexName, "id");
                await meilisearch.WaitForTaskAsync(createTask.TaskUid, cancellationToken: ct);

                var index = Index;
                await index.UpdateFilterableAttributesAsync(
                    ["tenantId", "categoryType", "inStock", "price", "brand", "sizes", "colors", "author"], ct);
                await index.UpdateSearchableAttributesAsync(
                    ["name", "description", "sku", "tags", "brand", "author"], ct);
                await index.UpdateSortableAttributesAsync(["price"], ct);
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex, "Failed to configure Meilisearch index {Index}; retrying in 5s.", meilisearchOptions.Value.IndexName);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    internal async Task HandleAsync(string? json, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        var envelope = JsonSerializer.Deserialize<DomainEventEnvelope>(json);
        if (envelope is null)
        {
            return;
        }

        switch (envelope.EventType)
        {
            case EventTypes.ProductCreated:
                await IndexAsync(BuildDocumentFromCreated(envelope), ct);
                break;
            case EventTypes.ProductUpdated:
                await IndexAsync(BuildDocumentFromUpdated(envelope), ct);
                break;
            case EventTypes.InventoryAdjusted:
                await UpdateStockAsync(BuildStockUpdateFromInventoryAdjusted(envelope), ct);
                break;
            case EventTypes.ProductDeleted:
                await RemoveAsync(BuildDeletionFromEnvelope(envelope), ct);
                break;
            default:
                logger.LogDebug("Ignoring unrecognized event type {EventType}", envelope.EventType);
                break;
        }
    }

    /// <summary>
    /// Pure envelope-to-document mapping, exposed `internal` (via
    /// InternalsVisibleTo, mirroring ProductSearchService.BuildFacetStage's
    /// rationale in CLAUDE.md) so the mapping logic is unit-testable without
    /// a live Meilisearch instance -- only the fact that it indexes
    /// correctly needs the real integration test.
    /// </summary>
    internal static ProductSearchDocument BuildDocumentFromCreated(DomainEventEnvelope envelope)
    {
        var payload = JsonSerializer.Deserialize<ProductCreatedPayload>(envelope.PayloadJson)
            ?? throw new InvalidOperationException($"'{envelope.EventType}' payload failed to deserialize.");

        return ToDocument(
            envelope.TenantId, payload.ProductId, payload.Sku, payload.Name, payload.Description,
            payload.CategoryType, payload.Price, payload.Currency, payload.InStock, payload.QuantityOnHand,
            payload.Tags, payload.Attributes);
    }

    internal static ProductSearchDocument BuildDocumentFromUpdated(DomainEventEnvelope envelope)
    {
        var payload = JsonSerializer.Deserialize<ProductUpdatedPayload>(envelope.PayloadJson)
            ?? throw new InvalidOperationException($"'{envelope.EventType}' payload failed to deserialize.");

        return ToDocument(
            envelope.TenantId, payload.ProductId, payload.Sku, payload.Name, payload.Description,
            payload.CategoryType, payload.Price, payload.Currency, payload.InStock, payload.QuantityOnHand,
            payload.Tags, payload.Attributes);
    }

    /// <summary>
    /// A partial document (only id + the two inventory-adjustment fields) --
    /// sent via Meilisearch's add-or-update ("update documents") endpoint,
    /// which merges by primary key instead of replacing the whole document,
    /// exactly like InventoryProjectionConsumer's targeted
    /// Builders&lt;T&gt;.Update for the same event. Anonymous-typed rather
    /// than a named record because it's a wire-shape detail private to this
    /// one call, not something any other project needs to agree on.
    /// </summary>
    internal static object BuildStockUpdateFromInventoryAdjusted(DomainEventEnvelope envelope)
    {
        var payload = JsonSerializer.Deserialize<InventoryAdjustedPayload>(envelope.PayloadJson)
            ?? throw new InvalidOperationException($"'{envelope.EventType}' payload failed to deserialize.");

        return new
        {
            id = ProductSearchDocument.DocumentId(envelope.TenantId, payload.ProductId),
            quantityOnHand = payload.NewQuantity,
            inStock = payload.InStock,
        };
    }

    internal static string BuildDeletionFromEnvelope(DomainEventEnvelope envelope)
    {
        var payload = JsonSerializer.Deserialize<ProductDeletedPayload>(envelope.PayloadJson)
            ?? throw new InvalidOperationException($"'{envelope.EventType}' payload failed to deserialize.");

        return ProductSearchDocument.DocumentId(envelope.TenantId, payload.ProductId);
    }

    private static ProductSearchDocument ToDocument(
        string tenantId,
        string productId,
        string sku,
        string name,
        string? description,
        string categoryType,
        decimal price,
        string currency,
        bool inStock,
        int quantityOnHand,
        List<string> tags,
        Dictionary<string, List<string>> attributes) => new()
        {
            Id = ProductSearchDocument.DocumentId(tenantId, productId),
            TenantId = tenantId,
            ProductId = productId,
            Sku = sku,
            Name = name,
            Description = description,
            CategoryType = categoryType,
            Price = price,
            Currency = currency,
            InStock = inStock,
            QuantityOnHand = quantityOnHand,
            Tags = tags,
            Brand = attributes.TryGetValue("brand", out var brand) ? brand.FirstOrDefault() : null,
            Sizes = attributes.TryGetValue("sizes", out var sizes) ? sizes : null,
            Colors = attributes.TryGetValue("colors", out var colors) ? colors : null,
            Author = attributes.TryGetValue("author", out var author) ? author.FirstOrDefault() : null,
        };

    private async Task IndexAsync(ProductSearchDocument document, CancellationToken ct)
    {
        await Index.AddDocumentsAsync([document], "id", ct);
        logger.LogInformation("Indexed {Sku} (tenant {TenantId}) into Meilisearch", document.Sku, document.TenantId);
    }

    private async Task UpdateStockAsync(object partialDocument, CancellationToken ct)
    {
        await Index.UpdateDocumentsAsync([partialDocument], "id", ct);
        logger.LogInformation("Updated stock fields in Meilisearch: {@Document}", partialDocument);
    }

    private async Task RemoveAsync(string documentId, CancellationToken ct)
    {
        await Index.DeleteOneDocumentAsync(documentId, ct);
        logger.LogInformation("Removed {DocumentId} from Meilisearch", documentId);
    }
}
