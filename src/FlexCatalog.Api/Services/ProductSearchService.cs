using System.Text.RegularExpressions;
using FlexCatalog.Api.Dtos;
using FlexCatalog.Api.Repositories;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace FlexCatalog.Api.Services;

public interface IProductSearchService
{
    Task<ProductSearchResponse> SearchAsync(ProductSearchRequest request, CancellationToken ct = default);
}

/// <summary>
/// Builds and runs a single MongoDB aggregation pipeline that returns a page
/// of results plus facet counts in one round trip via $facet. This is the
/// centerpiece of the "why Mongo" story for this repo: category-specific
/// attribute filters, price range, in-stock, and free-text search all
/// compose into one pipeline, and facet counts come back from the same
/// query instead of N follow-up queries.
///
/// Independent-branch faceting (ADR 0004): the category facet and each
/// attribute facet (brand/sizes/colors/author) are computed net of every
/// *other* filter but not their own -- selecting "Brand: Acme" still shows
/// the result counts for Sony and LG, so a UI can render "switch to Sony"
/// as an option rather than making it disappear. This means there is no
/// single shared $match before $facet any more: $text (if present) stays
/// the mandatory first pipeline stage (MongoDB doesn't allow $text inside
/// a $facet sub-pipeline), and every other filter is re-applied per
/// branch via BuildStructuredMatchDocument, with that branch's own filter
/// component excluded. The price-range facet is the one deliberate
/// exception: it stays net of the *full* filter (including the price
/// filter itself), since it describes "the price bounds of what's
/// currently visible", not a togglable option list -- see ADR 0004.
/// </summary>
public sealed class ProductSearchService(IProductRepository repository) : IProductSearchService
{
    private static readonly Regex SafeAttributeKey = new("^[a-zA-Z][a-zA-Z0-9]*$", RegexOptions.Compiled);

    private static readonly string[] FacetedAttributeFields = ["brand", "sizes", "colors", "author"];

    public async Task<ProductSearchResponse> SearchAsync(ProductSearchRequest request, CancellationToken ct = default)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var usesTextSearch = !string.IsNullOrWhiteSpace(request.Query);

        var pipeline = new List<BsonDocument>();

        if (usesTextSearch)
        {
            // Must be its own top-level $match, ahead of $facet -- see the
            // class doc comment above for why $text can't move into a
            // per-branch match the way every other filter now does.
            pipeline.Add(new BsonDocument("$match", BuildTextMatchDocument(request.Query!)));
            pipeline.Add(new BsonDocument("$addFields",
                new BsonDocument("textScore", new BsonDocument("$meta", "textScore"))));
        }

        pipeline.Add(BuildFacetStage(request, page, pageSize, usesTextSearch));
        pipeline.Add(BuildReshapeStage());

        var output = await repository.AggregateTenantScopedAsync(pipeline, ct)
                     ?? new BsonDocument
                     {
                         ["results"] = new BsonArray(),
                         ["totalCount"] = new BsonArray(),
                         ["categoryFacet"] = new BsonArray(),
                         ["attributeFacets"] = new BsonArray(),
                         ["priceRange"] = new BsonArray(),
                     };

        var items = output["results"].AsBsonArray
            .Select(doc => ProductResponse.FromDomain(BsonSerializer.Deserialize<Domain.Product>(doc.AsBsonDocument)))
            .ToList();

        var totalCount = output["totalCount"].AsBsonArray is { Count: > 0 } tc
            ? tc[0]["count"].ToInt64()
            : 0L;

        var categoryFacet = output["categoryFacet"].AsBsonArray
            .Select(d => new FacetCount(d["_id"].ToString()!, d["count"].ToInt32()))
            .ToList();

        var attributeFacets = new Dictionary<string, List<FacetCount>>();
        foreach (var entry in output["attributeFacets"].AsBsonArray)
        {
            var field = entry["field"].AsString;
            var counts = entry["counts"].AsBsonArray
                .Select(d => new FacetCount(d["_id"].ToString() ?? string.Empty, d["count"].ToInt32()))
                .Where(f => !string.IsNullOrEmpty(f.Value))
                .ToList();
            if (counts.Count > 0)
            {
                attributeFacets[field] = counts;
            }
        }

        var priceRangeArray = output["priceRange"].AsBsonArray;
        var priceRange = priceRangeArray.Count > 0
            ? new PriceRangeFacet(
                priceRangeArray[0]["min"].IsBsonNull ? null : priceRangeArray[0]["min"].ToDecimal(),
                priceRangeArray[0]["max"].IsBsonNull ? null : priceRangeArray[0]["max"].ToDecimal())
            : new PriceRangeFacet(null, null);

        return new ProductSearchResponse(
            items,
            totalCount,
            page,
            pageSize,
            new SearchFacets(categoryFacet, attributeFacets, priceRange));
    }

    /// <summary>
    /// Must be the pipeline's own top-level $match stage (see the class doc
    /// comment) -- never combined into BuildStructuredMatchDocument's
    /// output, and never used inside a $facet branch.
    /// </summary>
    internal static BsonDocument BuildTextMatchDocument(string query) =>
        new("$text", new BsonDocument("$search", query));

    /// <summary>
    /// Builds every filter except $text: category, price range, in-stock,
    /// and attribute filters. Used both for the "results"/"totalCount"/
    /// "priceRange" $facet branches (no exclusions -- the full filter) and
    /// for the independent-branch facets (excludeCategory for the category
    /// facet, excludeAttributeKey for that attribute's own facet), so each
    /// facet can show counts net of every *other* filter without seeing its
    /// own selection narrow itself away.
    /// </summary>
    internal static BsonDocument BuildStructuredMatchDocument(
        ProductSearchRequest request,
        bool excludeCategory = false,
        string? excludeAttributeKey = null)
    {
        var match = new BsonDocument();

        if (request.CategoryType.HasValue && !excludeCategory)
        {
            match["categoryType"] = request.CategoryType.Value.ToString();
        }

        if (request.InStock.HasValue)
        {
            match["inStock"] = request.InStock.Value;
        }

        if (request.MinPrice.HasValue || request.MaxPrice.HasValue)
        {
            var priceRange = new BsonDocument();
            if (request.MinPrice.HasValue)
            {
                priceRange["$gte"] = request.MinPrice.Value;
            }

            if (request.MaxPrice.HasValue)
            {
                priceRange["$lte"] = request.MaxPrice.Value;
            }

            match["price"] = priceRange;
        }

        if (request.Attributes is { Count: > 0 })
        {
            foreach (var (key, values) in request.Attributes)
            {
                // Reject anything that isn't a plain field name -- this is
                // what stops a caller turning a facet filter into an
                // operator injection (e.g. a key of "$where").
                if (!SafeAttributeKey.IsMatch(key) || values is not { Count: > 0 })
                {
                    continue;
                }

                if (excludeAttributeKey is not null && string.Equals(key, excludeAttributeKey, StringComparison.Ordinal))
                {
                    continue;
                }

                match[$"attributes.{key}"] = new BsonDocument("$in", new BsonArray(values));
            }
        }

        return match;
    }

    internal static BsonDocument BuildFacetStage(ProductSearchRequest request, int page, int pageSize, bool usesTextSearch)
    {
        var sort = request.SortBy switch
        {
            ProductSortOrder.PriceAsc => new BsonDocument("price", 1),
            ProductSortOrder.PriceDesc => new BsonDocument("price", -1),
            ProductSortOrder.Newest => new BsonDocument("createdAt", -1),
            ProductSortOrder.Relevance when usesTextSearch => new BsonDocument("textScore", -1),
            _ => new BsonDocument("_id", 1),
        };

        var resultsPipeline = new BsonArray
        {
            new BsonDocument("$match", BuildStructuredMatchDocument(request)),
            new BsonDocument("$sort", sort),
            new BsonDocument("$skip", (page - 1) * pageSize),
            new BsonDocument("$limit", pageSize),
        };

        var facetStage = new BsonDocument
        {
            ["results"] = resultsPipeline,
            ["totalCount"] = new BsonArray
            {
                new BsonDocument("$match", BuildStructuredMatchDocument(request)),
                new BsonDocument("$count", "count"),
            },
            // Independent-branch: net of every filter except the category
            // filter itself, so switching categories stays a visible option.
            ["categoryFacet"] = new BsonArray
            {
                new BsonDocument("$match", BuildStructuredMatchDocument(request, excludeCategory: true)),
                new BsonDocument("$group", new BsonDocument
                {
                    ["_id"] = "$categoryType",
                    ["count"] = new BsonDocument("$sum", 1),
                }),
            },
            // Deliberately net of the *full* filter, including the price
            // filter itself -- see the class doc comment.
            ["priceRange"] = new BsonArray
            {
                new BsonDocument("$match", BuildStructuredMatchDocument(request)),
                new BsonDocument("$group", new BsonDocument
                {
                    ["_id"] = BsonNull.Value,
                    ["min"] = new BsonDocument("$min", "$price"),
                    ["max"] = new BsonDocument("$max", "$price"),
                }),
            },
        };

        // Each candidate attribute field gets its own $facet branch (a
        // separate sub-pipeline), which is why FacetedAttributeFields is
        // kept short and deliberate rather than derived from whatever
        // fields happen to exist. Independent-branch: net of every filter
        // except this field's own attribute filter.
        foreach (var field in FacetedAttributeFields)
        {
            var branchMatch = BuildStructuredMatchDocument(request, excludeAttributeKey: field);
            branchMatch[$"attributes.{field}"] = new BsonDocument("$exists", true);

            facetStage[AttrBranchKey(field)] = new BsonArray
            {
                new BsonDocument("$match", branchMatch),
                new BsonDocument("$unwind", new BsonDocument
                {
                    ["path"] = $"$attributes.{field}",
                    ["preserveNullAndEmptyArrays"] = false,
                }),
                new BsonDocument("$group", new BsonDocument
                {
                    ["_id"] = $"$attributes.{field}",
                    ["count"] = new BsonDocument("$sum", 1),
                }),
            };
        }

        return new BsonDocument("$facet", facetStage);
    }

    private static string AttrBranchKey(string field) => $"attr_{field}";

    /// <summary>
    /// Combines the per-field attribute facet branches produced by $facet
    /// into a single stable "attributeFacets: [{field, counts}]" array, so
    /// the response shape doesn't depend on FacetedAttributeFields' contents.
    /// </summary>
    private static BsonDocument BuildReshapeStage()
    {
        var attributeFacetsArray = new BsonArray();
        foreach (var field in FacetedAttributeFields)
        {
            attributeFacetsArray.Add(new BsonDocument
            {
                ["field"] = field,
                ["counts"] = $"${AttrBranchKey(field)}",
            });
        }

        var project = new BsonDocument
        {
            ["results"] = 1,
            ["totalCount"] = 1,
            ["categoryFacet"] = 1,
            ["priceRange"] = 1,
            ["attributeFacets"] = attributeFacetsArray,
        };

        return new BsonDocument("$project", project);
    }
}
