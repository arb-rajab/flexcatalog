using System.Collections.Concurrent;
using System.Text.Json;
using FlexCatalog.Contracts.Eventing;
using FlexCatalog.IntegrationTests.Fixtures;
using FlexCatalog.InventoryProjector;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NATS.Client.Core;
using NATS.Net;

namespace FlexCatalog.IntegrationTests.Eventing;

/// <summary>
/// The load-bearing test for ADR 0008: proves the two behaviors that
/// distinguish a durable JetStream consumer from the plain core-NATS
/// subscription ADR 0006 originally shipped with.
///
/// 1. A consumer that is not running when an event publishes still
///    receives it once it (re)starts, as long as its durable consumer
///    already existed against the stream (i.e. it had run at least once
///    before going down) -- this is what "catch up" means for a durable
///    consumer, as opposed to core pub/sub's "gone if nobody was
///    listening."
/// 2. A message that can never successfully process (a malformed
///    payload) is retried a bounded number of times, then dead-lettered
///    via a structured error log rather than blocking every message
///    behind it forever.
///
/// Runs against the real `nats` Testcontainer (JetStream enabled by
/// default in Testcontainers.Nats) and a real, independent
/// <see cref="InventoryProjectionConsumer"/> instance -- publishing goes
/// straight to NATS (bypassing FlexCatalog.Api entirely), since what's
/// under test here is the consumer's subscription behavior, not the
/// publish path ADR 0006's own test already covers.
/// </summary>
[Collection("EventStreaming collection")]
public sealed class InventoryProjectionDurabilityTests(MongoContainerFixture mongoFixture, NatsContainerFixture natsFixture)
{
    [Fact]
    public async Task ConsumerDownDuringPublish_CatchesUpOnceRestarted()
    {
        var natsUrl = natsFixture.Container.GetConnectionString();
        var durableName = $"inventory-projector-catchup-{Guid.NewGuid():N}";
        var projections = ProjectionCollection();

        // First "process lifetime": starts long enough to register its
        // durable consumer against the stream, then goes down. A durable
        // consumer that has never run before wouldn't have a backlog to
        // catch up on -- this mirrors a redeploy/crash-and-restart, not a
        // brand-new consumer's first start.
        await using (var firstRunConnection = new NatsConnection(NatsOpts.Default with { Url = natsUrl }))
        {
            var firstRun = new InventoryProjectionConsumer(
                firstRunConnection, projections, Options.Create(NatsOptions(durableName)), NullLogger<InventoryProjectionConsumer>.Instance);
            await firstRun.StartAsync(CancellationToken.None);
            await WaitUntilConsumerIsReadyAsync(firstRunConnection, durableName);
            await firstRun.StopAsync(CancellationToken.None);
        }

        // The consumer is now fully down: no process, no connection.
        // Publish while nothing is subscribed -- exactly the scenario
        // ADR 0006 named as unrecoverable data loss under plain core NATS.
        var productId = $"catchup-{Guid.NewGuid():N}";
        var tenantId = $"tenant-{Guid.NewGuid():N}";
        await using (var publisherConnection = new NatsConnection(NatsOpts.Default with { Url = natsUrl }))
        {
            await publisherConnection.PublishAsync(
                "flexcatalog.events.product.created",
                JsonSerializer.Serialize(CreatedEnvelope(tenantId, productId)));
        }

        // Confirm it really wasn't processed live -- nothing was
        // subscribed to receive it.
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Null(await projections.Find(p => p.ProductId == productId).FirstOrDefaultAsync());

        // Second "process lifetime": a brand-new connection and a
        // brand-new InventoryProjectionConsumer instance, resuming the
        // same durable consumer name. This is the "comes back online"
        // half of the proof.
        await using var secondRunConnection = new NatsConnection(NatsOpts.Default with { Url = natsUrl });
        var secondRun = new InventoryProjectionConsumer(
            secondRunConnection, projections, Options.Create(NatsOptions(durableName)), NullLogger<InventoryProjectionConsumer>.Instance);
        await secondRun.StartAsync(CancellationToken.None);
        try
        {
            var projection = await WaitForProjectionAsync(projections, productId);
            Assert.Equal(tenantId, projection.TenantId);
            Assert.Equal(EventTypes.ProductCreated, projection.LastEventType);
        }
        finally
        {
            await secondRun.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task UnprocessableMessage_IsDeadLetteredWithoutBlockingSubsequentMessages()
    {
        var natsUrl = natsFixture.Container.GetConnectionString();
        var durableName = $"inventory-projector-deadletter-{Guid.NewGuid():N}";
        var projections = ProjectionCollection();
        var capturingLogger = new CapturingLogger<InventoryProjectionConsumer>();

        await using var connection = new NatsConnection(NatsOpts.Default with { Url = natsUrl });
        var consumer = new InventoryProjectionConsumer(
            connection, projections, Options.Create(NatsOptions(durableName)), capturingLogger);
        await consumer.StartAsync(CancellationToken.None);

        try
        {
            await WaitUntilConsumerIsReadyAsync(connection, durableName);

            // A payload that can never successfully deserialize --
            // BuildProjectionFromCreated throws every single delivery
            // attempt, exactly the "bug in a handler" poison-message case
            // ADR 0008's dead-letter handling exists for.
            var poisonEnvelope = new DomainEventEnvelope(
                Guid.NewGuid().ToString(), EventTypes.ProductCreated, "poison-tenant", DateTime.UtcNow, "not valid json");
            await connection.PublishAsync("flexcatalog.events.product.created", JsonSerializer.Serialize(poisonEnvelope));

            // A second, valid message right behind it -- proves the
            // poison message doesn't wedge the durable consumer for
            // everything published after it.
            var productId = $"after-poison-{Guid.NewGuid():N}";
            var tenantId = $"tenant-{Guid.NewGuid():N}";
            await connection.PublishAsync(
                "flexcatalog.events.product.created",
                JsonSerializer.Serialize(CreatedEnvelope(tenantId, productId)));

            var projection = await WaitForProjectionAsync(projections, productId);
            Assert.Equal(tenantId, projection.TenantId);

            await WaitUntilAsync(
                () => capturingLogger.ErrorMessages.Any(m => m.Contains("Dead-lettering", StringComparison.Ordinal)),
                "the poison message was never dead-lettered");

            // It stopped redelivering at the configured limit -- not
            // fewer (which would mean it gave up early) and not more
            // (which would mean it's still retrying forever).
            var deliveryAttemptErrors = capturingLogger.ErrorMessages.Count(m =>
                m.Contains("Failed to process message", StringComparison.Ordinal) ||
                m.Contains("Dead-lettering", StringComparison.Ordinal));
            Assert.Equal(InventoryProjectionConsumer.MaxDeliverAttempts, deliveryAttemptErrors);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    private IMongoCollection<ProductProjection> ProjectionCollection()
    {
        var mongoClient = new MongoClient(mongoFixture.Container.GetConnectionString());
        return mongoClient.GetDatabase($"flexcatalog_durability_test_{Guid.NewGuid():N}")
            .GetCollection<ProductProjection>("productInventoryProjection");
    }

    private static NatsOptions NatsOptions(string durableConsumerName) => new()
    {
        DurableConsumerName = durableConsumerName,
    };

    private static DomainEventEnvelope CreatedEnvelope(string tenantId, string productId) => new(
        Guid.NewGuid().ToString(),
        EventTypes.ProductCreated,
        tenantId,
        DateTime.UtcNow,
        JsonSerializer.Serialize(new ProductCreatedPayload(
            productId, "SKU-DURABILITY", "Durability Test Widget", "Electronics", 10m, "USD", 5, true,
            null, [], [])));

    /// <summary>
    /// Waits for the durable consumer to actually exist against the
    /// stream before either publishing (so the message is guaranteed to
    /// be captured) or tearing the first run down -- consumer/stream
    /// creation happens inside <see cref="InventoryProjectionConsumer.ExecuteAsync"/>,
    /// asynchronously relative to <c>StartAsync</c> returning.
    /// </summary>
    private static async Task WaitUntilConsumerIsReadyAsync(
        INatsConnection connection, string durableConsumerName, TimeSpan? timeout = null)
    {
        var js = connection.CreateJetStreamContext();
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await js.GetConsumerAsync("FLEXCATALOG_EVENTS", durableConsumerName);
                return;
            }
            catch
            {
                await Task.Delay(100);
            }
        }

        Assert.Fail($"Durable consumer '{durableConsumerName}' was never created.");
    }

    private static async Task<ProductProjection> WaitForProjectionAsync(
        IMongoCollection<ProductProjection> projections, string productId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(90));
        while (DateTime.UtcNow < deadline)
        {
            var candidate = await projections.Find(p => p.ProductId == productId).FirstOrDefaultAsync();
            if (candidate is not null)
            {
                return candidate;
            }

            await Task.Delay(200);
        }

        Assert.Fail($"Projection for product '{productId}' never appeared within the timeout.");
        return null!;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, string failureMessage, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(200);
        }

        Assert.Fail(failureMessage);
    }

    /// <summary>
    /// A real <see cref="ILogger{T}"/> (not a mock) that records every
    /// Error-level message so the test can assert on the dead-letter log
    /// line ADR 0008 relies on as the "inspectable record" of a failed
    /// message, and count exactly how many delivery attempts were logged.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> ErrorMessages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                ErrorMessages.Enqueue(formatter(state, exception));
            }
        }

        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
