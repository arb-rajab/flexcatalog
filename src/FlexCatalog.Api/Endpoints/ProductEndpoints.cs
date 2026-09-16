using FlexCatalog.Api.Dtos;
using FlexCatalog.Api.Services;

namespace FlexCatalog.Api.Endpoints;

public static class ProductEndpoints
{
    public const string AdminPolicy = "Admin";

    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products").WithTags("Products").RequireAuthorization();

        group.MapGet("/{id}", async (string id, IProductService service, CancellationToken ct) =>
                Results.Ok(await service.GetByIdAsync(id, ct)))
            .WithName("GetProduct");

        group.MapPost("/", async (UpsertProductRequest request, IProductService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"/api/products/{created.Id}", created);
            })
            .RequireAuthorization(AdminPolicy)
            .WithName("CreateProduct");

        group.MapPut("/{id}", async (string id, UpsertProductRequest request, IProductService service, CancellationToken ct) =>
                Results.Ok(await service.UpdateAsync(id, request, ct)))
            .RequireAuthorization(AdminPolicy)
            .WithName("UpdateProduct");

        group.MapDelete("/{id}", async (string id, IProductService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            })
            .RequireAuthorization(AdminPolicy)
            .WithName("DeleteProduct");

        group.MapPost("/{id}/inventory/adjust", async (string id, AdjustInventoryRequest request, IProductService service, CancellationToken ct) =>
                Results.Ok(await service.AdjustInventoryAsync(id, request.Delta, ct)))
            .RequireAuthorization(AdminPolicy)
            .WithName("AdjustInventory")
            .WithSummary("Adjust stock quantity by a signed delta; flips inStock automatically.");

        group.MapPost("/search", async (ProductSearchRequest request, IProductSearchService searchService, CancellationToken ct) =>
                Results.Ok(await searchService.SearchAsync(request, ct)))
            .WithName("SearchProducts")
            .WithSummary("Faceted search: category, price range, in-stock, free text, and category-specific attribute filters.");
    }
}
