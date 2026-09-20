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
}
