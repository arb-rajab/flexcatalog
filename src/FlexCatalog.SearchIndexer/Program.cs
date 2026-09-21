using FlexCatalog.SearchIndexer;
using Meilisearch;
using Microsoft.Extensions.Options;
using NATS.Client.Core;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<MeilisearchOptions>(builder.Configuration.GetSection(MeilisearchOptions.SectionName));
builder.Services.Configure<NatsOptions>(builder.Configuration.GetSection(NatsOptions.SectionName));

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value;
    return new MeilisearchClient(options.Url, options.ApiKey);
});

// One long-lived connection for the process's lifetime, mirroring
// FlexCatalog.InventoryProjector's own INatsConnection registration.
builder.Services.AddSingleton<INatsConnection>(sp =>
{
    var options = sp.GetRequiredService<IOptions<NatsOptions>>().Value;
    return new NatsConnection(NatsOpts.Default with { Url = options.Url });
});

builder.Services.AddHostedService<SearchIndexingConsumer>();

var host = builder.Build();
host.Run();
