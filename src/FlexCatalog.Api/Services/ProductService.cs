using FlexCatalog.Api.Domain;
using FlexCatalog.Api.Dtos;
using FlexCatalog.Api.Eventing;
using FlexCatalog.Api.Repositories;
using FlexCatalog.Contracts.Eventing;

namespace FlexCatalog.Api.Services;

public interface IProductService
{
    Task<ProductResponse> GetByIdAsync(string id, CancellationToken ct = default);

    Task<ProductResponse> CreateAsync(UpsertProductRequest request, CancellationToken ct = default);

    Task<ProductResponse> UpdateAsync(string id, UpsertProductRequest request, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);

    Task<ProductResponse> AdjustInventoryAsync(string id, int delta, CancellationToken ct = default);
}

public sealed class ProductService(IProductRepository repository, IDomainEventPublisher events) : IProductService
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

        // Fire-and-forget (ADR 0006): published after the write already
        // succeeded, using the tenantId InsertAsync just stamped onto
        // `product` from the validated JWT -- never a client-supplied value.
        events.Publish(EventTypes.ProductCreated, product.TenantId, new ProductCreatedPayload(
            product.Id, product.Sku, product.Name, product.CategoryType.ToString(),
            product.Price, product.Currency, product.QuantityOnHand, product.InStock,
            product.Description, product.Tags, FlattenAttributesForIndexing(product.Attributes)));

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

        // Fire-and-forget (ADR 0007, same discipline as ADR 0006): a search
        // index that never learns about edits would keep serving stale
        // results forever, so an update needs to be a real event too.
        events.Publish(EventTypes.ProductUpdated, existing.TenantId, new ProductUpdatedPayload(
            existing.Id, existing.Sku, existing.Name, existing.CategoryType.ToString(),
            existing.Price, existing.Currency, existing.QuantityOnHand, existing.InStock,
            existing.Description, existing.Tags, FlattenAttributesForIndexing(existing.Attributes)));

        return ProductResponse.FromDomain(existing);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        // Fetched first (rather than just checking DeleteAsync's bool
        // result, as before ADR 0007) because the ProductDeleted event needs
        // the tenantId/sku of what was just removed -- same
        // fetch-before-mutate shape UpdateAsync/AdjustInventoryAsync already
        // use, not a new pattern.
        var existing = await repository.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Product '{id}' not found.");

        var deleted = await repository.DeleteAsync(id, ct);
        if (!deleted)
        {
            throw new NotFoundException($"Product '{id}' not found.");
        }

        events.Publish(EventTypes.ProductDeleted, existing.TenantId, new ProductDeletedPayload(existing.Id, existing.Sku));
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

        var previousQuantity = existing.QuantityOnHand;
        existing.QuantityOnHand = newQuantity;
        existing.InStock = newQuantity > 0;
        existing.UpdatedAt = DateTime.UtcNow;

        await repository.ReplaceAsync(existing, ct);

        // Fire-and-forget (ADR 0006): published after the write already
        // succeeded, using the tenantId ReplaceAsync just stamped onto
        // `existing` from the validated JWT -- never a client-supplied value.
        events.Publish(EventTypes.InventoryAdjusted, existing.TenantId, new InventoryAdjustedPayload(
            existing.Id, existing.Sku, delta, previousQuantity, newQuantity, existing.InStock));

        return ProductResponse.FromDomain(existing);
    }

    /// <summary>
    /// Flattens the polymorphic <see cref="ProductAttributes"/> bag into the
    /// field-name -> values shape ProductCreatedPayload/ProductUpdatedPayload
    /// carry for FlexCatalog.SearchIndexer (ADR 0007), using the exact same
    /// field vocabulary as ProductSearchService.FacetedAttributeFields
    /// (brand/sizes/colors/author) so both search paths describe attributes
    /// the same way. Contracts can't reference this polymorphic Domain type
    /// directly, which is why the event payload carries the flattened form
    /// rather than the attribute bag itself.
    /// </summary>
    private static Dictionary<string, List<string>> FlattenAttributesForIndexing(ProductAttributes attributes)
    {
        var flattened = new Dictionary<string, List<string>>();

        switch (attributes)
        {
            case ElectronicsAttributes electronics:
                if (!string.IsNullOrWhiteSpace(electronics.Brand))
                {
                    flattened["brand"] = [electronics.Brand];
                }

                break;
            case ApparelAttributes apparel:
                if (apparel.Sizes.Count > 0)
                {
                    flattened["sizes"] = apparel.Sizes;
                }

                if (apparel.Colors.Count > 0)
                {
                    flattened["colors"] = apparel.Colors;
                }

                break;
            case BookAttributes book:
                if (!string.IsNullOrWhiteSpace(book.Author))
                {
                    flattened["author"] = [book.Author];
                }

                break;
        }

        return flattened;
    }
}
