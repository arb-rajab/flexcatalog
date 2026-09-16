using FlexCatalog.Api.Domain;
using FlexCatalog.Api.Dtos;
using FlexCatalog.Api.Repositories;

namespace FlexCatalog.Api.Services;

public interface IProductService
{
    Task<ProductResponse> GetByIdAsync(string id, CancellationToken ct = default);

    Task<ProductResponse> CreateAsync(UpsertProductRequest request, CancellationToken ct = default);

    Task<ProductResponse> UpdateAsync(string id, UpsertProductRequest request, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);

    Task<ProductResponse> AdjustInventoryAsync(string id, int delta, CancellationToken ct = default);
}

public sealed class ProductService(IProductRepository repository) : IProductService
{
    public async Task<ProductResponse> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var product = await repository.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Product '{id}' not found.");
        return ProductResponse.FromDomain(product);
    }

    public async Task<ProductResponse> CreateAsync(UpsertProductRequest request, CancellationToken ct = default)
    {
        ProductValidation.EnsureAttributesMatchCategory(request.CategoryType, request.Attributes);

        var existing = await repository.GetBySkuAsync(request.Sku, ct);
        if (existing is not null)
        {
            throw new ConflictException($"SKU '{request.Sku}' already exists for this tenant.");
        }

        var product = new Product
        {
            Sku = request.Sku,
            Name = request.Name,
            Description = request.Description,
            CategoryType = request.CategoryType,
            Price = request.Price,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency,
            InStock = request.InStock,
            QuantityOnHand = request.QuantityOnHand,
            Tags = request.Tags ?? [],
            Attributes = request.Attributes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await repository.InsertAsync(product, ct);
        return ProductResponse.FromDomain(product);
    }

    public async Task<ProductResponse> UpdateAsync(string id, UpsertProductRequest request, CancellationToken ct = default)
    {
        ProductValidation.EnsureAttributesMatchCategory(request.CategoryType, request.Attributes);

        var existing = await repository.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Product '{id}' not found.");

        var skuOwner = await repository.GetBySkuAsync(request.Sku, ct);
        if (skuOwner is not null && skuOwner.Id != id)
        {
            throw new ConflictException($"SKU '{request.Sku}' already exists for this tenant.");
        }

        existing.Sku = request.Sku;
        existing.Name = request.Name;
        existing.Description = request.Description;
        existing.CategoryType = request.CategoryType;
        existing.Price = request.Price;
        existing.Currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency;
        existing.InStock = request.InStock;
        existing.QuantityOnHand = request.QuantityOnHand;
        existing.Tags = request.Tags ?? [];
        existing.Attributes = request.Attributes;
        existing.UpdatedAt = DateTime.UtcNow;

        var replaced = await repository.ReplaceAsync(existing, ct);
        if (!replaced)
        {
            throw new NotFoundException($"Product '{id}' not found.");
        }

        return ProductResponse.FromDomain(existing);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var deleted = await repository.DeleteAsync(id, ct);
        if (!deleted)
        {
            throw new NotFoundException($"Product '{id}' not found.");
        }
    }

    public async Task<ProductResponse> AdjustInventoryAsync(string id, int delta, CancellationToken ct = default)
    {
        var existing = await repository.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Product '{id}' not found.");

        var newQuantity = existing.QuantityOnHand + delta;
        if (newQuantity < 0)
        {
            throw new ValidationException(
                $"Cannot adjust quantity by {delta}: current quantity is {existing.QuantityOnHand}.");
        }

        existing.QuantityOnHand = newQuantity;
        existing.InStock = newQuantity > 0;
        existing.UpdatedAt = DateTime.UtcNow;

        await repository.ReplaceAsync(existing, ct);
        return ProductResponse.FromDomain(existing);
    }
}
