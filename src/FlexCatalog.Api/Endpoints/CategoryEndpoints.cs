using FlexCatalog.Api.Domain;

namespace FlexCatalog.Api.Endpoints;

public static class CategoryEndpoints
{
    /// <summary>
    /// Static, code-defined metadata describing each category's attribute
    /// shape. Not persisted -- category shapes are part of the API contract
    /// (see the ProductAttributes hierarchy), not tenant-editable config, so
    /// a database collection would be a layer with no purpose.
    /// </summary>
    private static readonly object CategoryMetadata = new[]
    {
        new
        {
            categoryType = nameof(CategoryType.Electronics),
            attributeFields = new[] { "brand", "model", "warrantyMonths", "specs" },
            facetableFields = new[] { "brand" },
        },
        new
        {
            categoryType = nameof(CategoryType.Apparel),
            attributeFields = new[] { "sizes", "colors", "material", "gender" },
            facetableFields = new[] { "sizes", "colors" },
        },
        new
        {
            categoryType = nameof(CategoryType.Books),
            attributeFields = new[] { "author", "isbn", "pages", "format" },
            facetableFields = new[] { "author" },
        },
    };

    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/categories", () => Results.Ok(CategoryMetadata))
            .RequireAuthorization()
            .WithTags("Categories")
            .WithName("ListCategories")
            .WithSummary("Category types and their attribute shapes, for building search UIs.");
    }
}
