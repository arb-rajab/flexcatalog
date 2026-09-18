# ADR 0004: Faceted Search via a Single Aggregation Pipeline

- Status: Accepted; amended 2026-09-17 (independent-branch faceting)
- Date: 2026-09-16

## Context

`POST /api/products/search` needs to support, in one call: a category
filter, a price range, an in-stock filter, free-text search, an arbitrary
set of category-specific attribute filters (brand, size, color, author,
...), pagination, sorting, and facet counts (how many results per
category / per brand / the price range of the result set) so a UI can
render filter chips with counts next to them. This is the feature that
most directly exercises MongoDB's aggregation framework and is the
clearest demonstration of "why Mongo" for this repo, so it's worth being
explicit about the design, not just the fact that `$facet` was used.

## Decision

**One aggregation pipeline per search request**, built by
`ProductSearchService` and always run through
`ProductRepository.AggregateTenantScopedAsync` (which prepends the tenant
`$match` -- ADR 0001):

```
$match          (tenant, prepended by the repository)
$match          ($text, only when a text query is present -- MUST stay a
                 top-level stage; MongoDB does not allow $text inside a
                 $facet sub-pipeline, which is why it can't move into the
                 per-branch $match stages below)
$addFields      (textScore, only when a $text query is present)
$facet
  results:        [$match(full struct. filter), $sort, $skip, $limit]
  totalCount:     [$match(full struct. filter), $count]
  categoryFacet:  [$match(struct. filter, category excluded), $group by categoryType]
  priceRange:     [$match(full struct. filter), $group: min/max price]
  attr_brand:     [$match(struct. filter, brand excluded, +exists), $unwind, $group]
  attr_sizes:     [$match(struct. filter, sizes excluded, +exists), $unwind, $group]
  attr_colors:    [$match(struct. filter, colors excluded, +exists), $unwind, $group]
  attr_author:    [$match(struct. filter, author excluded, +exists), $unwind, $group]
$project        (reshape the attr_* branches into attributeFacets: [{field, counts}])
```

"struct. filter" = category / price / inStock / attribute filters, built
by `ProductSearchService.BuildStructuredMatchDocument` -- everything
except `$text`, which lives in its own top-level stage as shown above.

This returns the page of results *and* every facet count in one round
trip to MongoDB, instead of the page query plus one follow-up query per
facet (which is what the equivalent relational implementation would look
like: one `SELECT ... LIMIT/OFFSET` plus one `SELECT category, COUNT(*)
GROUP BY category` plus one such query per attribute).

Attribute filter keys from the request are validated against
`^[a-zA-Z][a-zA-Z0-9]*$` before being turned into a Mongo field path
(`attributes.{key}`) or dropped -- this is what stops a caller from
supplying a filter key like `$where` or `brand.$ne` and having it
interpreted as a Mongo operator rather than a literal field name. This is
tested directly (`ProductSearchServiceQueryBuildingTests
.BuildMatchDocument_RejectsUnsafeAttributeKeys`).

### Resolved (2026-09-17): facets are now independent-branch, with one deliberate exception

A "fully independent" faceted-nav UI computes each facet's counts against
every filter *except that facet's own* -- so that selecting "Brand: Acme"
still shows you the counts for Sony and LG (so you know switching brands
is possible), rather than only ever showing counts for the brand you
already selected. That requires one aggregation branch per facet with a
*different* `$match` per branch (facet X's branch excludes filter X).

This was originally shipped as a documented simplification (every facet
computed against the same, fully-applied `$match`) and has since been
implemented as designed: the category facet and each attribute facet
(brand/sizes/colors/author) now get their own per-branch `$match`, built
by `BuildStructuredMatchDocument(request, excludeCategory: ...)` /
`BuildStructuredMatchDocument(request, excludeAttributeKey: ...)`, so
each shows counts net of every *other* filter but not its own. Covered by
`ProductSearchServiceQueryBuildingTests` (pipeline shape, no DB needed)
and `SearchFacetsTests.Search_AttributeFacet_IsIndependentOfItsOwnFilter`
/ `Search_CategoryFacet_IsIndependentOfCategoryFilter` (end-to-end, real
Mongo).

**One deliberate exception: the price-range facet stays net of the full
filter**, including the price filter itself. Unlike category/attribute
facets, which are discrete option lists a UI toggles between, price range
is a continuous "what are the bounds of what's currently visible"
indicator -- there's no meaningful "show me the price bounds as if the
price filter weren't applied" UI affordance the way there is for
"show me the other brands". Revisit if a future UI wants a fixed,
never-narrowing slider range instead.

This was also the source of the pipeline restructuring noted above: since
independent-branch facets each need their own filter, MongoDB's rule that
`$text` can't appear inside a `$facet` sub-pipeline forced `$text` out to
its own dedicated top-level `$match` stage (previously it was combined
with every other filter in one shared top-level `$match`).

### Known simplification: attribute facets are a fixed, short field list

`FacetedAttributeFields = ["brand", "sizes", "colors", "author"]` is
hand-picked, not derived from the wildcard index or from scanning
existing documents. Faceting (grouping + counting distinct values) on an
arbitrary, unbounded field set is a fundamentally different cost profile
from *filtering* on one (which the wildcard index from ADR 0002 makes
cheap for any field) -- an unbounded facet list means an unbounded number
of `$group` branches per query. Keeping this list short and explicit is
the honest tradeoff; growing it is a one-line change per field, not an
architectural change.

## Consequences

- Every search request is exactly one round trip to MongoDB, regardless
  of how many filters or facets are requested. Independent-branch
  faceting does mean more `$match` stages *within* that one round trip
  (one per facet branch, each re-evaluating the structured filter minus
  its own component) -- more work per query than the original
  fully-shared-`$match` design, but still one round trip, and each
  branch's `$match` is cheap relative to the `$group`/`$unwind` work
  beside it.
- The pipeline-building logic (`BuildStructuredMatchDocument`,
  `BuildTextMatchDocument`, `BuildFacetStage`) is exposed `internal` with
  `InternalsVisibleTo` specifically so it can be unit-tested as pure
  BSON-document-building logic, without needing a live MongoDB connection
  for every test of filter/injection/sort/exclusion behavior -- see
  `tests/FlexCatalog.UnitTests/Services
  /ProductSearchServiceQueryBuildingTests.cs`. End-to-end correctness
  (does the query actually return the right documents, and the right
  *facet* documents, from a real mongod) is covered separately by the
  integration tests.
- Sorting by relevance only applies a meaningful order when a `$text`
  query is present (via `$meta: "textScore"`); with no text query,
  "relevance" falls back to `_id` ascending, which is stable but not
  meaningful -- documented behavior, not a bug.

## Alternatives considered and rejected

- **N+1 queries (one for results, one per facet)**: simplest to write,
  but throws away the reason to use `$facet` at all and multiplies
  database round trips linearly with the number of facets requested.
- **A dedicated search engine (Elasticsearch/Atlas Search)**: the right
  answer for large-scale, relevance-tuned, typo-tolerant search. Rejected
  here because it would obscure the point of this repo (MongoDB's own
  aggregation capabilities) behind a second system, and is disproportionate
  to a portfolio-scale catalog.
- **Independent-branch faceting from the start**: originally rejected for
  the first iteration as unnecessary complexity for the demo's initial
  scope; implemented in a follow-up session once the core `$facet`
  pattern was already demonstrated (see "Resolved" above).
