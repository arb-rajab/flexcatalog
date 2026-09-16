using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace FlexCatalog.Api.Domain;

/// <summary>
/// Base type for the category-specific attribute bag stored on a
/// <see cref="Product"/>. Serialized polymorphically by the MongoDB driver
/// using a discriminator field ("_t"), so each product document only carries
/// the fields relevant to its own category -- no wide table of nullable
/// columns, no EAV table, no per-category migration. Also polymorphic over
/// JSON (System.Text.Json) using a parallel "kind" discriminator, so API
/// clients can round-trip the same shape.
/// </summary>
[BsonDiscriminator(RootClass = true)]
[BsonKnownTypes(typeof(ElectronicsAttributes), typeof(ApparelAttributes), typeof(BookAttributes))]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ElectronicsAttributes), "electronics")]
[JsonDerivedType(typeof(ApparelAttributes), "apparel")]
[JsonDerivedType(typeof(BookAttributes), "books")]
public abstract class ProductAttributes
{
}

public sealed class ElectronicsAttributes : ProductAttributes
{
    public string? Brand { get; set; }

    public string? Model { get; set; }

    public int? WarrantyMonths { get; set; }

    /// <summary>
    /// Free-form spec sheet (e.g. "RAM" -> "16GB", "ScreenSizeInches" -> "15.6").
    /// Different electronics sub-types (laptops, headphones, cameras) have
    /// genuinely different spec fields, so this stays a dictionary rather than
    /// fixed properties.
    /// </summary>
    public Dictionary<string, string> Specs { get; set; } = new();
}

public sealed class ApparelAttributes : ProductAttributes
{
    public List<string> Sizes { get; set; } = new();

    public List<string> Colors { get; set; } = new();

    public string? Material { get; set; }

    public string? Gender { get; set; }
}

public sealed class BookAttributes : ProductAttributes
{
    public string? Author { get; set; }

    public string? Isbn { get; set; }

    public int? Pages { get; set; }

    public string? Format { get; set; }
}
