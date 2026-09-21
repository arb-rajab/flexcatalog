# ADR 0007: Meilisearch Search Indexer, Fed by the NATS Event Stream

- Status: Accepted
- Date: 2026-09-21

## Context

Two things are true about search in this repo before this change:

1. `POST /api/products/search` (ADR 0004) is a real, working faceted-search
   implementation over MongoDB's `$facet` aggregation -- category filters,
   price range, in-stock, attribute filters, and facet counts, all in one
   round trip. It is not broken and this ADR does not replace it.
2. Nothing in the portfolio demonstrates a *dedicated search engine* --
   typo tolerance, relevance ranking tuned for free text, sub-100ms search-
   as-you-type latency independent of the primary datastore's query
   planner. MongoDB's `$text` operator (which `$facet`'s `results`/
   `totalCount` branches use for the free-text part of a query) is a
   basic inverted-index text search: no typo tolerance, no fuzzy matching,
   and its relevance ranking is far less tunable than a purpose-built
   search engine's. That gap -- not a defect in ADR 0004 -- is what this
   ADR closes.

Separately, ADR 0006 added a real domain-event stream (NATS core pub/sub,
`ProductCreated`/`InventoryAdjusted` published fire-and-forget after each
MongoDB write) and a first independent consumer of it,
`FlexCatalog.InventoryProjector`, which builds its own MongoDB read-model
purely from the event stream. That consumer established a reusable shape:
an independent process, subscribed to `flexcatalog.events.>`, building its
own view of the world, decoupled from the API process and from every other
consumer. A search indexer that keeps a dedicated search engine in sync
with the catalog is architecturally the same shape -- "independent
consumer builds its own downstream store from the event stream" -- with a
search index instead of a MongoDB collection as the downstream store. This
ADR reuses that shape deliberately rather than inventing a second sync
mechanism (e.g. a poller that periodically re-reads MongoDB and diffs
against the index), which would duplicate ADR 0006's plumbing for no
benefit and would reintroduce polling latency/load that the event stream
already exists to avoid.

## Decision

**Meilisearch, fed by a new `FlexCatalog.SearchIndexer` worker service that
is a sibling of `FlexCatalog.InventoryProjector`, both subscribing to the
same NATS event stream.**

### Why Meilisearch over the alternatives

- **Elasticsearch / OpenSearch**: the canonical, most powerful answer for
  "dedicated search engine" -- and the wrong size for this portfolio
  entry. Both are JVM services with real memory floors (Elasticsearch's
  default heap alone is typically 1-2GB+), multi-minute cold starts under
  Testcontainers, and an operational surface (cluster health, shard
  allocation, index lifecycle management) that has nothing to teach here
  beyond "a search engine exists in the stack." Standing one up would make
  `docker-compose.yml` and CI's integration-test startup meaningfully
  heavier while demonstrating a smaller fraction of the product surface
  per unit of operational weight than Meilisearch does. This is the same
  proportionality argument ADR 0006 made for NATS over Kafka.
- **MongoDB Atlas Search**: not applicable -- this repo deliberately runs
  a self-hosted `mongo:7.0` container (ADR 0002), not Atlas, and Atlas
  Search isn't available against a self-hosted deployment. Reaching for it
  would mean either a cloud dependency this portfolio doesn't otherwise
  have, or abandoning self-hosted Mongo entirely -- out of scope for
  adding a search demonstration.
- **Algolia / Typesense**: Algolia is hosted-SaaS-only, which doesn't fit
  a self-contained `docker-compose up` demo the way every other piece of
  this stack does (Mongo, NATS, and now Meilisearch are all
  self-hostable). Typesense is a reasonable peer to Meilisearch on
  footprint and would have been a defensible choice too; Meilisearch was
  picked for its first-class official .NET client (`Meilisearch` on
  NuGet) matching this repo's C#/.NET stack the way `NATS.Client.Core`
  and `MongoDB.Driver` already do, and for being the more widely
  recognized reference point for "lightweight, typo-tolerant, single
  static-binary search engine" in the current ecosystem.
- **Meilisearch**: a single Rust binary, starts in about a second, default
  configuration already gives typo tolerance, prefix search, and tunable
  relevance ranking rules out of the box -- no analyzer/mapping
  configuration required to get a good result, which is the opposite of
  Elasticsearch's operational model. Small enough footprint that adding
  it to `docker-compose.yml` and CI costs about the same as NATS did in
  ADR 0006, for a capability (real typo-tolerant full-text search) that
  MongoDB's `$text` genuinely does not provide.

### Reusing the event-stream pattern, not inventing a second one

`FlexCatalog.SearchIndexer` subscribes to `flexcatalog.events.>` on the
same NATS broker `FlexCatalog.InventoryProjector` already uses, with the
identical retry/backoff discipline (`SubscribeAsync` connects eagerly, so
a broker outage at startup is caught and retried on a fixed 5s backoff
rather than crashing the host -- see `SearchIndexingConsumer.ExecuteAsync`,
copied verbatim in shape from `InventoryProjectionConsumer.ExecuteAsync`).
It never queries FlexCatalog.Api's MongoDB `products` collection, and
`InventoryProjectionConsumer` itself was not touched -- this is a new,
independent sibling consumer, not a modification to the existing one.

### Events: two reused, two added

| Event | Status | Why |
|---|---|---|
| `product.created` | Reused, payload enriched | `ProductCreatedPayload` already existed for `InventoryProjector`; this ADR adds `Description`, `Tags`, and a flattened `Attributes` bag (`{"brand": ["Acme"]}`-shaped, matching `ProductSearchService.FacetedAttributeFields`'s own vocabulary) so a consumer can build a *searchable* document without a follow-up read. `InventoryProjector`'s own consumer code is untouched and simply ignores the new fields, the same way it already ignored `Price`/`Currency`. |
| `inventory.adjusted` | Reused, unchanged | Already carries everything a stock-only reindex needs (`NewQuantity`, `InStock`); `SearchIndexingConsumer` sends it to Meilisearch as a partial document update (merge by primary key), mirroring how `InventoryProjectionConsumer` already does a targeted Mongo `$set` update for the same event. |
| `product.updated` | **New** | `ProductService.UpdateAsync` existed before this change but published nothing -- a search index that never learned about edits would keep serving stale name/price/description forever. Added via the exact same fire-and-forget hook pattern as `CreateAsync`/`AdjustInventoryAsync` (`events.Publish(...)` immediately after the MongoDB write succeeds, never wrapped around it): no new publishing mechanism, just the same one attached to a write path that hadn't used it yet. |
| `product.deleted` | **New** | `ProductService.DeleteAsync` existed but published nothing either, for the same reason `product.updated` was missing: a document is now fetched before the delete (mirroring the fetch-before-mutate shape `UpdateAsync`/`AdjustInventoryAsync` already use) specifically so the event can carry the tenantId/sku of what was removed. Without this, a deleted product would stay searchable forever -- index correctness, not just index freshness, requires it. |

All four payload types live in `FlexCatalog.Contracts.Eventing`, the same
project ADR 0006 established as the shared schema source of truth between
the publisher and every independent consumer.

### The Meilisearch document and tenant isolation

One shared index (`products`), one document per product, `id` =
`"{tenantId}:{productId}"` (`ProductSearchDocument.DocumentId`) -- the same
shared-collection-plus-discriminator shape ADR 0001 chose for MongoDB over
database-per-tenant or collection-per-tenant, for the identical reason:
a handful of tenants at demo scale doesn't justify per-tenant index
provisioning, and the discriminator only works if every query is forced
to include it.

That forcing happens structurally, not by caller convention, mirroring
`ProductRepository.AggregateTenantScopedAsync`:
`MeilisearchProductSearchService.BuildFilter` unconditionally ANDs
`tenantId = "..."` onto every query, and that value comes only from
`ITenantContext` (JWT-derived -- ADR 0001 again), never from anything a
caller supplies in the request body. There is no code path in
`FlexCatalog.Api` that queries the Meilisearch index other than through
this one service.

`ProductSearchDocument` itself lives in `FlexCatalog.Contracts.Search`, not
duplicated once per project: `FlexCatalog.SearchIndexer` is the sole
writer, `FlexCatalog.Api` a read-only client, and both need to agree on
the same document shape -- the identical reasoning ADR 0006 gave for
`DomainEventEnvelope` living in the shared Contracts project rather than
being redefined on each side.

### Additive search endpoint

`POST /api/products/search/meilisearch` is new and additive next to the
existing `POST /api/products/search` -- neither replaces the other:

| | `POST /api/products/search` (ADR 0004) | `POST /api/products/search/meilisearch` (this ADR) |
|---|---|---|
| Engine | MongoDB `$facet` aggregation | Meilisearch |
| Best for | Faceted filter-chip navigation (facet counts per category/brand/etc., independent-branch faceting) | A free-text search box: typo tolerance, prefix matching, relevance-tuned ranking |
| Free text | MongoDB `$text` (exact-token inverted index, no typo tolerance) | Meilisearch's fuzzy/typo-tolerant matching |
| Facet counts | Yes -- the entire point of that endpoint | No -- this endpoint returns hits only |
| Data source | `products` collection directly (source of truth) | The Meilisearch index (eventually consistent, built from events -- see Consequences) |

A real product UI would use both: the faceted endpoint for the filter
sidebar, this one for the search-as-you-type box at the top of the page.

### Read/write key separation (documented, not fully implemented)

`FlexCatalog.SearchIndexer` writes with Meilisearch's master key (it
creates the index, configures its settings, and adds/updates/deletes
documents -- it needs full admin rights). `FlexCatalog.Api` only ever
searches. In this demo both processes are configured with the same key
(`FLEXCATALOG_MEILI_MASTER_KEY`, one `docker-compose.yml` environment
variable) for setup simplicity; a production deployment would instead
create a search-only Meilisearch API key (scoped to the `search` action
via Meilisearch's key-management endpoints) for the API process, so a
compromised API instance could never write to or reconfigure the index.
Documented at the point of use in `Api/Search/MeilisearchOptions.cs`.

## Consequences

- **Eventually consistent, same caveat as ADR 0006's projection.** Between
  a write succeeding (HTTP response returned) and the event reaching
  `SearchIndexer` and landing in Meilisearch, `/search/meilisearch` can
  return stale or missing results for that product. MongoDB remains the
  source of truth (via `GET /api/products/{id}` and the `$facet` search
  endpoint); the Meilisearch index is a convenience read path layered on
  top, not something anything else in the system depends on for
  correctness.
- **At-most-once delivery, no replay**, inherited directly from ADR 0006's
  NATS-core choice: if `SearchIndexer` is down when an event publishes,
  that event is gone and the index silently drifts from MongoDB until the
  next write to that same product. Same accepted tradeoff ADR 0006
  already made and documented; the upgrade path (NATS JetStream, a
  durable consumer) is identical for this consumer too.
- **Two more moving parts in `docker-compose.yml` and CI**: a
  `meilisearch` container and a `search-indexer` process, plus a new
  `Testcontainers`-based `MeilisearchContainerFixture` (no official
  `Testcontainers.Meilisearch` package exists yet, so this uses the
  generic `Testcontainers` builder the same way `NatsContainerFixture`
  wraps `Testcontainers.Nats`) for the integration suite. Meilisearch's
  image is small and starts in about a second, so this doesn't
  meaningfully change CI runtime, consistent with ADR 0006's equivalent
  observation about the `nats` container.
- **`ProductCreatedPayload`'s shape changed** (three fields added:
  `Description`, `Tags`, `Attributes`). `InventoryProjector`'s own
  `InventoryProjectionConsumer.cs` was deliberately not touched -- it
  continues to compile and run correctly because it only ever read a
  subset of the payload's fields (already true before this change, for
  `Price`/`Currency`), and System.Text.Json's constructor-parameter
  binding is name-based, not positional, so adding fields doesn't disturb
  existing (de)serialization.
- **Attribute vocabulary is deliberately narrow** (`brand`/`sizes`/
  `colors`/`author`), reusing exactly
  `ProductSearchService.FacetedAttributeFields`'s existing four names
  rather than introducing a new one. Same "known simplification, revisit
  if a real UI needs more" posture ADR 0004 already took for that list.

## Alternatives considered and rejected

- **Elasticsearch / OpenSearch**: right tool at real scale; disproportionate
  operational weight for this portfolio entry (see above).
- **A poller re-reading MongoDB on an interval and diffing against the
  index**: would duplicate ADR 0006's event-publishing plumbing with a
  second, weaker sync mechanism (higher latency, wasted read load, more
  code) instead of reusing the event stream that already exists for
  exactly this purpose.
- **Replacing `/api/products/search` with the Meilisearch endpoint**:
  rejected outright by this task's own scope -- the two solve different
  problems (facet counts vs. typo-tolerant free text) and MongoDB remains
  the system's source of truth; removing the facet endpoint would also
  remove its independent-branch faceting entirely, with no Meilisearch
  equivalent shipped here.
- **Giving `FlexCatalog.Api` the same admin master key permanently as a
  final design** (rather than a documented simplification): rejected as
  the *intended* end state -- documented in `Search/MeilisearchOptions.cs`
  and above as a known simplification with a clear, low-effort upgrade
  path (a scoped search-only key), not left unstated.
