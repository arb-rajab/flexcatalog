namespace FlexCatalog.Api.Eventing;

public sealed class NatsOptions
{
    public const string SectionName = "Nats";

    public string Url { get; set; } = "nats://localhost:4222";

    public string SubjectPrefix { get; set; } = "flexcatalog.events";
}
