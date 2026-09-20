using FlexCatalog.Contracts.Eventing;
using Microsoft.Extensions.Options;
using NATS.Client.Core;

namespace FlexCatalog.Api.Eventing;

/// <summary>
/// Drains <see cref="DomainEventChannel"/> and publishes each envelope to
/// NATS. Registered as a Singleton <see cref="IHostedService"/> depending
/// only on other Singletons (<see cref="DomainEventChannel"/>,
/// <see cref="IOptions{TOptions}"/>) -- see CLAUDE.md's note on hosted
/// services with Scoped dependencies not being caught by a plain build.
///
/// Every failure here (NATS unreachable, connection reset, etc.) is caught
/// and logged, never rethrown: this loop must keep draining the channel for
/// the life of the app regardless of the broker's availability, and a
/// publish failure must never be visible to the request that already
/// completed its MongoDB write (ADR 0005).
/// </summary>
public sealed class DomainEventPublishingService(
    DomainEventChannel channel,
    IOptions<NatsOptions> natsOptions,
    ILogger<DomainEventPublishingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = natsOptions.Value;
        await using var connection = new NatsConnection(NatsOpts.Default with { Url = options.Url });

        await foreach (var envelope in channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var subject = $"{options.SubjectPrefix}.{envelope.EventType}";
                await connection.PublishAsync(
                    subject,
                    System.Text.Json.JsonSerializer.Serialize(envelope),
                    cancellationToken: stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "Failed to publish domain event {EventType} (tenant {TenantId}) to NATS; the originating write already succeeded and is unaffected.",
                    envelope.EventType, envelope.TenantId);
            }
        }
    }
}
