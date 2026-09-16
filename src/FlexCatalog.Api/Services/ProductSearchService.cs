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
/// compose into one $match, and facet counts come back from the same query
/// instead of N follow-up queries.
///
/// Known simplification (documented in docs/project-memory/architecture.md):
/// facet counts are computed against the fully-applied filter, not
/// "net of their own filter" as a fully independent faceted-nav UI would
/// want (e.g. selecting a brand narrows the brand facet's own counts too).
/// True independent-branch faceting is a backlog item, not implemented here.
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

        var matchDoc = BuildMatchDocument(request, usesTextSearch);

        var pipeline = new List<BsonDocument> { new("$match", matchDoc) };

        if (usesTextSearch)
        {
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

    internal static BsonDocument BuildMatchDocument(ProductSearchRequest request, bool usesTextSearch)
    {
        var match = new BsonDocument();

        if (request.CategoryType.HasValue)
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

        if (usesTextSearch)
        {
            match["$text"] = new BsonDocument("$search", request.Query);
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
            new BsonDocument("$sort", sort),
            new BsonDocument("$skip", (page - 1) * pageSize),
            new BsonDocument("$limit", pageSize),
        };

        var facetStage = new BsonDocument
        {
            ["results"] = resultsPipeline,
            ["totalCount"] = new BsonArray { new BsonDocument("$count", "count") },
            ["categoryFacet"] = new BsonArray
            {
                new BsonDocument("$group", new BsonDocument
                {
                    ["_id"] = "$categoryType",
                    ["count"] = new BsonDocument("$sum", 1),
                }),
            },
            ["priceRange"] = new BsonArray
            {
                new BsonDocument("$group", new BsonDocument
                {
                    ["_id"] = BsonNull.Value,
                    ["min"] = new BsonDocument("$min", "$price"),
                    ["max"] = new BsonDocument("$max", "$price"),
                }),
            },
        };

        // Each candidate attribute field gets its own $facet branch (a
        // separate sub-pipeline run against the same $match output), which
        // is why FacetedAttributeFields is kept short and deliberate rather
        // than derived from whatever fields happen to exist.
        foreach (var field in FacetedAttributeFields)
        {
            facetStage[AttrBranchKey(field)] = new BsonArray
            {
                new BsonDocument("$match", new BsonDocument($"attributes.{field}", new BsonDocument("$exists", true))),
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
