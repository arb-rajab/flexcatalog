using FlexCatalog.InventoryProjector;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NATS.Client.Core;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<MongoOptions>(builder.Configuration.GetSection(MongoOptions.SectionName));
builder.Services.Configure<NatsOptions>(builder.Configuration.GetSection(NatsOptions.SectionName));

builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
    return new MongoClient(options.ConnectionString);
});

builder.Services.AddSingleton(sp =>
{
    var client = sp.GetRequiredService<IMongoClient>();
    var options = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
    return client.GetDatabase(options.DatabaseName).GetCollection<ProductProjection>("productInventoryProjection");
});

// One long-lived connection for the process's lifetime; NatsConnection
// implements IAsyncDisposable, which the host's DI container disposes on
// shutdown.
builder.Services.AddSingleton<INatsConnection>(sp =>
{
    var options = sp.GetRequiredService<IOptions<NatsOptions>>().Value;
    return new NatsConnection(NatsOpts.Default with { Url = options.Url });
});

builder.Services.AddHostedService<InventoryProjectionConsumer>();

var host = builder.Build();
host.Run();
