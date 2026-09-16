using FlexCatalog.Api.Domain;

namespace FlexCatalog.Api.Services;

/// <summary>
/// Guards the one invariant a schema-flexible document store can't enforce
/// for us: that a product's attribute bag actually matches its declared
/// category (e.g. no Electronics product carrying ApparelAttributes).
/// </summary>
public static class ProductValidation
{
    public static void EnsureAttributesMatchCategory(CategoryType categoryType, ProductAttributes attributes)
    {
        var valid = categoryType switch
        {
            CategoryType.Electronics => attributes is ElectronicsAttributes,
            CategoryType.Apparel => attributes is ApparelAttributes,
            CategoryType.Books => attributes is BookAttributes,
            _ => false,
        };

        if (!valid)
        {
            throw new ValidationException(
                $"Category '{categoryType}' requires matching attributes of type " +
                $"{ExpectedTypeName(categoryType)}, but got {attributes.GetType().Name}.");
        }
    }

    private static string ExpectedTypeName(CategoryType categoryType) => categoryType switch
    {
        CategoryType.Electronics => nameof(ElectronicsAttributes),
        CategoryType.Apparel => nameof(ApparelAttributes),
        CategoryType.Books => nameof(BookAttributes),
        _ => "Unknown",
    };
}
