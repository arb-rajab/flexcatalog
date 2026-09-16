using MongoDB.Driver;

namespace FlexCatalog.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", async (IMongoClient client, CancellationToken ct) =>
        {
            try
            {
                await client.GetDatabase("admin").RunCommandAsync<MongoDB.Bson.BsonDocument>(
                    new MongoDB.Bson.BsonDocument("ping", 1), cancellationToken: ct);
                return Results.Ok(new { status = "healthy" });
            }
            catch (Exception ex)
            {
                return Results.Json(new { status = "unhealthy", error = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .AllowAnonymous()
        .WithTags("Health")
        .WithName("HealthCheck");
    }
}
