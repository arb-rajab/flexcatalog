namespace FlexCatalog.InventoryProjector;

public sealed class NatsOptions
{
    public const string SectionName = "Nats";

    public string Url { get; set; } = "nats://localhost:4222";

    /// <summary>
    /// Matches FlexCatalog.Api's Eventing.NatsOptions.SubjectPrefix -- the
    /// projector subscribes to "{SubjectPrefix}.&gt;" (every event type under
    /// the prefix), so the two only need to agree on this string, not share
    /// a type.
    /// </summary>
    public string SubjectPrefix { get; set; } = "flexcatalog.events";

    /// <summary>
    /// The JetStream stream capturing every subject under
    /// <see cref="SubjectPrefix"/> (ADR 0008). Shared with
    /// FlexCatalog.SearchIndexer -- either service can create it, since
    /// <c>CreateOrUpdateStreamAsync</c> is idempotent and service start
    /// order isn't guaranteed.
    /// </summary>
    public string StreamName { get; set; } = "FLEXCATALOG_EVENTS";

    /// <summary>
    /// This service's own durable JetStream consumer name (ADR 0008).
    /// Must stay stable across restarts/redeploys -- it's the identity
    /// JetStream uses to resume from wherever this consumer last
    /// acknowledged, rather than replaying from the start every time.
    /// </summary>
    public string DurableConsumerName { get; set; } = "inventory-projector";
}
