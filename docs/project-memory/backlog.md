# Backlog

Deferred work, in rough priority order, with the reason each was deferred
rather than done now.

## Security hardening
1. **Refuse to start in `Production` if `Jwt:Secret` matches the known
   development placeholder value.** **Done** (this session) --
   `JwtSecretGuard.EnsureNotPlaceholder`, called from `Program.cs`,
   pinned by `JwtSecretGuardTests`. Closes R2 in `risk.md`.
2. **Rate limiting on `POST /api/auth/login`** (ASP.NET Core's built-in
   rate limiting middleware is enough). **Done** (this session) -- fixed
   window, 5 attempts/60s per client IP, 429 on excess
   (`LoginRateLimiting`, wired in `Program.cs`/`AuthEndpoints.cs`), pinned
   by `LoginRateLimitingTests` (unit) and an integration test asserting
   the 6th rapid attempt gets 429. Closes R4.
3. **JWT revocation / short-lived-token-plus-refresh-token model.**
   **Re-affirmed as explicitly deferred this session** (not half-shipped):
   evaluated and consciously not attempted, rather than starting a
   partial implementation under time pressure. "Fully done" here would
   require, at minimum: a revocation store (in Mongo or a cache) checked
   on every authenticated request, a refresh-token issuance/rotation
   endpoint, refresh-token storage with reuse detection, and updated
   integration coverage in `TenantIsolationTests`-adjacent tests to prove
   revocation can't be bypassed cross-tenant -- each a meaningful, review-
   worthy change in its own right, not something to bolt on inside a
   session already touching startup/auth wiring. Current mitigation
   remains the capped 60-minute token lifetime (R3), which is an accepted
   tradeoff for this system's scope, not a gap silently left unmentioned.
   Next session: pick *one* of (a) revocation-only via a short deny-list,
   or (b) full refresh-token flow -- don't attempt both at once.

## Search
4. **Independent-branch faceted navigation** (ADR 0004). **Done** (this
   session) -- the category facet and each attribute facet (brand/sizes/
   colors/author) are now computed net of every filter except their own,
   so switching a brand filter still shows the other available brands'
   counts; `ProductSearchService.BuildStructuredMatchDocument`'s
   `excludeCategory`/`excludeAttributeKey` parameters, pinned by
   `ProductSearchServiceQueryBuildingTests` (pipeline shape) and two new
   `SearchFacetsTests` (end-to-end, real Mongo). The price-range facet
   stays net of the full filter -- a deliberate, documented exception
   (ADR 0004), not an oversight.
5. **Configurable/derived facet field list**, instead of the current
   hardcoded `["brand", "sizes", "colors", "author"]`. Would need a
   bounded-cardinality check (faceting on a field with thousands of
   distinct values is a performance trap) before being safe to open up.

## Multi-tenancy scale path
6. **Database-per-tenant migration path**, for if/when a tenant's data
   volume or isolation requirements outgrow the shared-collection model
   (ADR 0001 documents this as the intended next step, not a hypothetical
   one). **Design doc added this session, not implemented** -- concrete
   trigger conditions and a per-tenant (not flag-day) migration approach
   are in ADR 0005, Part 2, including the specific reason `DataSeeder`/
   `MongoIndexInitializer` are the nontrivial part (they're exactly where
   the two DI-lifetime bugs in `handoff.md` originated).
7. **Platform-admin, cross-tenant reporting role.** Explicitly out of
   scope today (ADR 0001) -- the repository layer has no method that
   queries across tenants, by design; adding one would be a deliberate,
   separately-reviewed change.

## Operability
8. **Structured logging + basic OpenTelemetry tracing.** Would be the
   first thing added in a real production-hardening pass (R9). **Design
   doc added this session, not implemented** -- see ADR 0005, Part 1, for
   the proposed approach (JSON console logging, W3C trace-context
   correlation, OpenTelemetry via the MongoDB driver's existing
   `DiagnosticSource` support) and instrumentation priority order. Held
   back from implementation specifically because it's best verified by
   observing real emitted traces/logs, which needs infra this sandbox
   doesn't have (see `testing.md`) -- attempting it build-only risked the
   same half-shipped outcome item 3 was deliberately avoiding.
9. **Seed-data reset endpoint or CLI** for demo/QA environments, instead
   of relying on "seeding only happens once, when the tenants collection
   is empty."

## Product features (contrived-scope, lowest priority)
10. A fourth category (e.g. Groceries) purely to further demonstrate that
    adding a category is a code-only change (ADR 0002) -- not needed to
    prove the point (three categories already do), listed for
    completeness.
11. Bulk product import/export.
12. Soft-delete instead of hard-delete for products (audit trail).

## Testing
13. Load/performance test against a larger synthetic catalog, to put a
    number on how the wildcard attribute index (ADR 0002) behaves at
    scale rather than asserting it qualitatively.
14. Contract tests against the generated OpenAPI document.

## Event streaming (ADR 0006)
15. **Move the `flexcatalog.events.*` subjects onto NATS JetStream**, if a
    consumer ever needs at-least-once delivery or replay-from-a-point
    instead of today's best-effort core-NATS pub/sub. Deferred at
    introduction specifically because JetStream's ack/retry semantics are
    stronger than requirement #4 asked for on the primary write path --
    same client library and subjects, an additive change, not a rewrite.
    **Done** (ADR 0008) -- both `InventoryProjector` and `SearchIndexer`
    now consume via durable JetStream consumers; the publish side is
    unchanged plain core `PublishAsync`, exactly as anticipated here.
16. **A second, genuinely different consumer** (e.g. a tenant-level
    activity feed, or a webhook-fanout service) to demonstrate that the
    same event stream supports multiple independent subscribers, not just
    one. **Done** (ADR 0007) -- `FlexCatalog.SearchIndexer` is that second
    consumer (builds a Meilisearch index instead of a MongoDB read-model),
    proving the same stream fans out to genuinely different downstream
    stores without either consumer knowing about the other.
17. **Outbox pattern instead of an in-memory channel** between the
    repository write and the NATS publish, if this ever needs to survive
    an API process crash between the two. Deferred because the in-memory
    channel is consistent with the deliberately best-effort, at-most-once
    delivery guarantee this iteration chose (ADR 0006) -- an outbox would
    upgrade that guarantee, which isn't free (it requires the event and
    the domain write to commit in the same MongoDB transaction).

## Search indexing (ADR 0007)
18. **Meilisearch search-only API key for `FlexCatalog.Api`**, instead of
    today's shared master key (documented as a known simplification in
    `Search/MeilisearchOptions.cs` and ADR 0007). Low-effort follow-up --
    `MeilisearchClient.CreateKeyAsync` scoped to the `search` action --
    deferred only because it needs a real running Meilisearch instance to
    create the key against, which this sandbox can't reach (see
    `testing.md`).
19. **Move `flexcatalog.events.*` onto JetStream for `SearchIndexer` too**,
    same rationale and same deferred status as backlog item 15 -- a search
    index silently drifting after a missed event is the same accepted
    tradeoff as the inventory projection's, not a new one introduced here.
    **Done** (ADR 0008), alongside item 15 -- same durable-consumer/
    dead-letter shape, different durable consumer name.
20. **Configurable/derived Meilisearch filterable-attribute list**, same
    shape as backlog item 5 for the Mongo facet endpoint -- today's
    `brand`/`sizes`/`colors`/`author` set is hardcoded and deliberately
    reuses `ProductSearchService.FacetedAttributeFields`'s own vocabulary.
