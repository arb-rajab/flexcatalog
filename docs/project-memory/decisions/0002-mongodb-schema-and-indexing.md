# ADR 0002: MongoDB Schema Design and Indexing Strategy

- Status: Accepted
- Date: 2026-09-16

## Context

FlexCatalog's catalog spans categories with genuinely different attribute
shapes: an electronics product has `brand`, `warrantyMonths`, and a
free-form spec sheet (`RAM`, `screen size`, ...); an apparel product has
`sizes`, `colors`, `material`, `gender`; a book has `author`, `isbn`,
`pages`, `format`. None of these overlap in any meaningful way, and the
list of categories is expected to keep growing.

This is the actual reason this project uses MongoDB rather than a
relational database, so the schema design needs to make that difference
show up in the code, not just be asserted in prose. A relational schema
for this would force one of:

- A wide `products` table with a nullable column per attribute across
  every category ever added (`brand`, `warranty_months`, `sizes`,
  `colors`, `material`, `gender`, `author`, `isbn`, ... all on one row,
  nearly all NULL for any given product), which gets worse with every
  new category and makes "what does a book actually have" undiscoverable
  from the schema.
- An EAV (entity-attribute-value) side table (`product_id, attr_name,
  attr_value`), which recovers flexibility but loses types, loses the
  ability to index a specific attribute cheaply, and turns "give me all
  products where size=M and color=Red" into a self-join.
- A `JSONB`/`json` blob column, which is what most teams actually reach
  for once they hit this wall -- at which point they've reimplemented a
  document store's schema flexibility on top of a relational database,
  without that database's native tooling for indexing or querying inside
  the document.

## Decision

### Document shape: polymorphic attribute bag, not a flat wide document

`Product` carries the fields every category shares (`sku`, `name`,
`price`, `currency`, `inStock`, `quantityOnHand`, `tags`, `categoryType`)
plus one `attributes` field. `attributes` holds a category-specific
subtype (`ElectronicsAttributes`, `ApparelAttributes`, `BookAttributes`),
serialized polymorphically by the MongoDB C# driver via a `_t`
discriminator field (`BsonDiscriminator`/`BsonKnownTypes`). A book
document's `attributes` subdocument only ever contains book fields; an
electronics document's only ever contains electronics fields. Adding a
fourth category (say, Groceries with `weightGrams` and `expiryDate`) is a
new C# class and a new discriminator value -- no migration, no `ALTER
TABLE`, no change to any existing document.

`ProductValidation.EnsureAttributesMatchCategory` is the one guard this
flexibility gives up for free elsewhere: MongoDB's document model won't
stop a caller from saving an Electronics-categorized product with Apparel
attributes, so the application enforces that invariant explicitly at the
service layer (see `src/FlexCatalog.Api/Services/ProductValidation.cs`).
This is a deliberate, honest tradeoff -- the schema buys flexibility, and
the one thing that flexibility can't self-police is enforced in code
instead.

### Naming convention

A `ConventionPack` registers `CamelCaseElementNameConvention` for every
class in the `FlexCatalog.Api.Domain` namespace that isn't explicitly
mapped with `[BsonElement]`. This makes `ElectronicsAttributes.Brand`
serialize as `attributes.brand` in MongoDB, matching the `brand` name
`System.Text.Json` already produces for the same property over HTTP.
Search filters and facet keys are therefore the same string on both sides
of the API boundary -- a client never has to translate `Brand` (C#) to
`brand` (wire) to `Brand` (Mongo) through three different casing rules.

### Indexing strategy

Every index on `products` is tenant-first (compound, `tenantId` as the
leading key) -- see ADR 0001 for why every query is tenant-scoped, which
makes `tenantId` the correct leading key for every index that supports a
query shape this API actually issues:

| Index | Key | Supports |
|---|---|---|
| `tenant_sku_unique` | `{tenantId, sku}` unique | SKU lookup, create-time duplicate check |
| `tenant_category_price` | `{tenantId, categoryType, price}` | Browse-a-category-sorted-by-price, the most common catalog browsing shape |
| `tenant_instock` | `{tenantId, inStock}` | The "in stock only" filter |
| `tenant_attributes_wildcard` | `{tenantId, "attributes.$**"}` | **Compound wildcard index** (MongoDB 7+) covering every field under `attributes`, for every category, without a hand-maintained index per field per category |
| `tenant_text_search` | `{tenantId, name: "text", description: "text"}` | Free-text search (`$text`) |
| `username_unique` | `{username}` unique, not tenant-scoped | Login lookup (see ADR 0003) |
| `slug_unique` | `{slug}` unique on `tenants` | Tenant lookup by slug |

The wildcard index is the one genuinely MongoDB-specific choice here: it
is what makes "filter electronics by brand" and "filter apparel by size"
both index-backed without maintaining a growing, hand-written list of
per-category, per-field indexes as categories are added. A relational
schema has no equivalent -- you'd either index every nullable attribute
column individually (paying the write cost on every row regardless of
category) or give up and table-scan.

## Consequences

- Every new attribute field automatically benefits from the wildcard
  index; no index migration is needed when a category gains a field.
- The wildcard index does cost more to maintain on write than a single
  targeted index would, and (as of MongoDB 7/8) a compound wildcard index
  can only pair the wildcard field with non-wildcard fields that precede
  it in the key -- `tenantId` first, `attributes.$**` second is exactly
  that constraint, and it's the reason the field order in the index
  definition is not arbitrary.
- Facet computation in the search endpoint (ADR 0004) is still limited to
  a fixed, deliberately short list of facetable fields
  (`brand`, `sizes`, `colors`, `author`) rather than "whatever fields
  exist" -- the wildcard index makes *filtering* on any attribute cheap,
  but *faceting* (grouping + counting) on an unbounded field set is a
  different cost profile and is scoped down intentionally; see
  `backlog.md`.
- `MongoIndexInitializer` creates these indexes idempotently on startup
  (`CreateOneAsync`/`CreateManyAsync` no-op if an equivalent index already
  exists). For a system this size, that is simpler and more honest than a
  separate migration tool -- there is no schema to migrate, only indexes
  to guarantee exist.

## Alternatives considered and rejected

- **One index per known attribute field** (`attributes.brand`,
  `attributes.sizes`, `attributes.author`, ...): works, but requires
  remembering to add an index every time a category gains a field, and
  silently degrades to a collection scan for any field someone forgot.
  The wildcard index removes that maintenance burden entirely.
- **A flat, category-agnostic `attributes: Dictionary<string,string>`**
  instead of typed subclasses: would also index cleanly under the same
  wildcard index, but throws away C#'s type system for every attribute
  read/write path (API request validation, response typing) for no
  indexing benefit over the polymorphic approach actually used.
