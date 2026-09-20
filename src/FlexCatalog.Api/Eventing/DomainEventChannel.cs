using System.Threading.Channels;
using FlexCatalog.Contracts.Eventing;

namespace FlexCatalog.Api.Eventing;

/// <summary>
/// The in-memory hand-off point between the request path and
/// <see cref="DomainEventPublishingService"/>. Bounded and configured to
/// drop the oldest queued event rather than block a writer -- see ADR 0006:
/// publishing a domain event must never be able to slow down or fail the
/// MongoDB write it follows. Registered as a Singleton so the Scoped
/// <see cref="ChannelDomainEventPublisher"/> (used from request-scoped
/// services) and the Singleton background service share the same channel.
/// </summary>
public sealed class DomainEventChannel
{
    private readonly Channel<DomainEventEnvelope> _channel = Channel.CreateBounded<DomainEventEnvelope>(
        new BoundedChannelOptions(capacity: 1024)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    public ChannelWriter<DomainEventEnvelope> Writer => _channel.Writer;

    public ChannelReader<DomainEventEnvelope> Reader => _channel.Reader;
}
