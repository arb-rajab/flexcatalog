using FlexCatalog.Api.Domain;
using FlexCatalog.Api.Dtos;
using FlexCatalog.Api.Repositories;
using FlexCatalog.Api.Services;
using Moq;

namespace FlexCatalog.UnitTests.Services;

public class ProductServiceTests
{
    private readonly Mock<IProductRepository> _repository = new();
    private readonly ProductService _service;

    public ProductServiceTests()
    {
        _service = new ProductService(_repository.Object);
    }

    private static UpsertProductRequest ElectronicsRequest(string sku = "SKU-1") => new(
        sku, "Widget", "desc", CategoryType.Electronics, 9.99m, "USD", true, 5, null,
        new ElectronicsAttributes { Brand = "Acme" });

    [Fact]
    public async Task CreateAsync_WithMismatchedAttributes_ThrowsValidationException()
    {
        var request = new UpsertProductRequest(
            "SKU-1", "Widget", null, CategoryType.Apparel, 9.99m, "USD", true, 5, null,
            new ElectronicsAttributes { Brand = "Acme" });

        await Assert.ThrowsAsync<ValidationException>(() => _service.CreateAsync(request));

        _repository.Verify(r => r.InsertAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateSku_ThrowsConflictException()
    {
        _repository.Setup(r => r.GetBySkuAsync("SKU-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Product { Sku = "SKU-1" });

        await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(ElectronicsRequest()));
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_InsertsAndReturnsProduct()
    {
        _repository.Setup(r => r.GetBySkuAsync("SKU-1", It.IsAny<CancellationToken>())).ReturnsAsync((Product?)null);

        var result = await _service.CreateAsync(ElectronicsRequest());

        Assert.Equal("SKU-1", result.Sku);
        _repository.Verify(r => r.InsertAsync(It.Is<Product>(p => p.Sku == "SKU-1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ThrowsNotFoundException()
    {
        _repository.Setup(r => r.GetByIdAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync((Product?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetByIdAsync("missing"));
    }

    [Fact]
    public async Task AdjustInventoryAsync_NegativeResultingQuantity_ThrowsValidationException()
    {
        _repository.Setup(r => r.GetByIdAsync("p1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Product { Id = "p1", QuantityOnHand = 2 });

        await Assert.ThrowsAsync<ValidationException>(() => _service.AdjustInventoryAsync("p1", -5));
    }

    [Fact]
    public async Task AdjustInventoryAsync_ReducingToZero_SetsInStockFalse()
    {
        _repository.Setup(r => r.GetByIdAsync("p1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Product { Id = "p1", QuantityOnHand = 5, InStock = true });
        _repository.Setup(r => r.ReplaceAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.AdjustInventoryAsync("p1", -5);

        Assert.Equal(0, result.QuantityOnHand);
        Assert.False(result.InStock);
    }

    [Fact]
    public async Task AdjustInventoryAsync_IncreasingFromZero_SetsInStockTrue()
    {
        _repository.Setup(r => r.GetByIdAsync("p1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Product { Id = "p1", QuantityOnHand = 0, InStock = false });
        _repository.Setup(r => r.ReplaceAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.AdjustInventoryAsync("p1", 3);

        Assert.Equal(3, result.QuantityOnHand);
        Assert.True(result.InStock);
    }

    [Fact]
    public async Task DeleteAsync_WhenRepositoryReportsNoMatch_ThrowsNotFoundException()
    {
        _repository.Setup(r => r.DeleteAsync("p1", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.DeleteAsync("p1"));
    }
}
