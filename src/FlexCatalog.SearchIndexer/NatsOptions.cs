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
}
