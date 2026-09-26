namespace FlexCatalog.SearchIndexer;

public sealed class NatsOptions
{
    public const string SectionName = "Nats";

    public string Url { get; set; } = "nats://localhost:4222";

    /// <summary>
    /// Matches FlexCatalog.Api's Eventing.NatsOptions.SubjectPrefix and
    /// FlexCatalog.InventoryProjector's own copy of this same class -- every
    /// subscriber only needs to agree on this string, not share a type
    /// (see InventoryProjector's NatsOptions for why).
    /// </summary>
    public string SubjectPrefix { get; set; } = "flexcatalog.events";

    /// <summary>
    /// The JetStream stream capturing every subject under
    /// <see cref="SubjectPrefix"/> (ADR 0008). Shared with
    /// FlexCatalog.InventoryProjector -- either service can create it,
    /// since <c>CreateOrUpdateStreamAsync</c> is idempotent and service
    /// start order isn't guaranteed.
    /// </summary>
    public string StreamName { get; set; } = "FLEXCATALOG_EVENTS";

    /// <summary>
    /// This service's own durable JetStream consumer name (ADR 0008).
    /// Must stay stable across restarts/redeploys -- it's the identity
    /// JetStream uses to resume from wherever this consumer last
    /// acknowledged, rather than replaying from the start every time.
    /// </summary>
    public string DurableConsumerName { get; set; } = "search-indexer";
}
