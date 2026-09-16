using FlexCatalog.Api.Domain;

namespace FlexCatalog.Api.Dtos;

public sealed record UpsertProductRequest(
    string Sku,
    string Name,
    string? Description,
    CategoryType CategoryType,
    decimal Price,
    string Currency,
    bool InStock,
    int QuantityOnHand,
    List<string>? Tags,
    ProductAttributes Attributes);

public sealed record ProductResponse(
    string Id,
    string Sku,
    string Name,
    string? Description,
    CategoryType CategoryType,
    decimal Price,
    string Currency,
    bool InStock,
    int QuantityOnHand,
    List<string> Tags,
    ProductAttributes Attributes,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static ProductResponse FromDomain(Product p) => new(
        p.Id, p.Sku, p.Name, p.Description, p.CategoryType, p.Price, p.Currency,
        p.InStock, p.QuantityOnHand, p.Tags, p.Attributes, p.CreatedAt, p.UpdatedAt);
}

public sealed record AdjustInventoryRequest(int Delta);
