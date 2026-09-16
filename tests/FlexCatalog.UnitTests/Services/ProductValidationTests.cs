using FlexCatalog.Api.Domain;
using FlexCatalog.Api.Services;

namespace FlexCatalog.UnitTests.Services;

public class ProductValidationTests
{
    [Theory]
    [InlineData(CategoryType.Electronics, typeof(ElectronicsAttributes))]
    [InlineData(CategoryType.Apparel, typeof(ApparelAttributes))]
    [InlineData(CategoryType.Books, typeof(BookAttributes))]
    public void EnsureAttributesMatchCategory_AllowsMatchingShape(CategoryType category, Type attributesType)
    {
        var attributes = (ProductAttributes)Activator.CreateInstance(attributesType)!;

        var exception = Record.Exception(() => ProductValidation.EnsureAttributesMatchCategory(category, attributes));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(CategoryType.Electronics)]
    [InlineData(CategoryType.Apparel)]
    public void EnsureAttributesMatchCategory_RejectsBookAttributesForNonBookCategory(CategoryType category)
    {
        var attributes = new BookAttributes { Author = "Someone" };

        Assert.Throws<ValidationException>(() =>
            ProductValidation.EnsureAttributesMatchCategory(category, attributes));
    }

    [Fact]
    public void EnsureAttributesMatchCategory_RejectsElectronicsAttributesForApparel()
    {
        var attributes = new ElectronicsAttributes { Brand = "Acme" };

        Assert.Throws<ValidationException>(() =>
            ProductValidation.EnsureAttributesMatchCategory(CategoryType.Apparel, attributes));
    }
}
