namespace FlexCatalog.Api.Domain;

/// <summary>
/// Product categories supported by the catalog. Each category has a distinct
/// attribute shape (see <see cref="ProductAttributes"/> subclasses) -- this is
/// the reason the catalog is modeled on MongoDB rather than a fixed relational
/// schema: adding a new category with new fields never requires a migration.
/// </summary>
public enum CategoryType
{
    Electronics,
    Apparel,
    Books
}
