# Testing

## Strategy

Two test projects, matching the two things worth testing differently:

- **`FlexCatalog.UnitTests`** -- pure logic, no external dependencies, no
  Docker required. Runs anywhere `dotnet test` runs, including this
  sandbox.
- **`FlexCatalog.IntegrationTests`** -- full HTTP-request-to-real-MongoDB
  behavior, via `WebApplicationFactory<Program>` and a MongoDB container
  started per test class by Testcontainers. Requires Docker. Since ADR
  0006, one suite in here (`InventoryEventStreamingTests`) also spins up a
  real NATS container and a real, directly-instantiated
  `FlexCatalog.InventoryProjector.InventoryProjectionConsumer` to prove the
  event-streaming path end to end. Since ADR 0007, a second such suite
  (`SearchIndexingEventStreamingTests`) additionally spins up a real
  Meilisearch container and a real, directly-instantiated
  `FlexCatalog.SearchIndexer.SearchIndexingConsumer` -- see below.

## What's covered

Unit tests (69 tests at time of writing):
- `JwtSecretGuardTests` -- the Production placeholder-secret startup
  guard (R2): throws only when both "is Production" and "is exactly the
  known placeholder" are true.
- `LoginRateLimitingTests` -- pins the exact login rate-limit threshold
  (R4) by exercising the same `FixedWindowRateLimiter` configuration
  `Program.cs` wires up, and that separate partitions (IPs) don't share
  an allowance.
- `ProductValidationTests` -- category/attribute-shape matching
  invariant (ADR 0002).
- `ProductSearchServiceQueryBuildingTests` -- the aggregation
  pipeline-building logic (`BuildStructuredMatchDocument`,
  `BuildTextMatchDocument`, `BuildFacetStage`) exercised as pure
  BSON-document construction: category/price/stock filters, attribute
  filter `$in` clauses, **rejection of unsafe attribute filter keys**
  (operator-injection prevention, ADR 0004), pagination math, sort-order
  mapping, and (independent-branch faceting) that each facet branch's
  `$match` excludes only its own filter component while keeping every
  other filter. These are exposed `internal` + `InternalsVisibleTo`
  specifically so this logic is testable without a live database.
- `ProductServiceTests` -- create/update/delete/inventory-adjust business
  rules (duplicate SKU -> 409, mismatched attributes -> 400, negative
  inventory -> 400, not-found -> 404), using Moq against
  `IProductRepository`; since ADR 0006, also asserts `CreateAsync`/
  `AdjustInventoryAsync` publish the right domain event on success and
  publish nothing when validation rejects the request first, using Moq
  against `IDomainEventPublisher` -- no NATS needed for this part, only the
  end-to-end integration test below touches a real broker. Since ADR 0007,
  also asserts `UpdateAsync`/`DeleteAsync` publish `ProductUpdated`/
  `ProductDeleted` (previously they published nothing), and that the
  polymorphic `ProductAttributes` bag is correctly flattened into the
  event payload's `Attributes` dictionary for each category.
- `SearchIndexingConsumerMappingTests` (ADR 0007) -- the pure envelope-to-
  `ProductSearchDocument` mapping logic (`BuildDocumentFromCreated`,
  `BuildDocumentFromUpdated`, `BuildStockUpdateFromInventoryAdjusted`,
  `BuildDeletionFromEnvelope`), exposed `internal` + `InternalsVisibleTo`
  the same way `ProductSearchService`'s pipeline-building methods are, so
  this is testable without a live Meilisearch: category-specific attribute
  flattening (brand/sizes+colors/author), the tenant-scoped document id
  scheme, and the partial-update shape for inventory adjustments.
- `MeilisearchProductSearchServiceTests` (ADR 0007) -- the pure Meilisearch
  filter-building logic (`BuildFilter`): the tenant filter is always
  present and always comes first, and category/price/stock filters
  compose onto it correctly. This is where a tenant-isolation regression
  in the Meilisearch-backed search path would actually show up.
- `JwtTokenServiceTests` -- issued tokens carry the correct `tenant_id` /
  `role` / `sub` claims and expiry.
- `PasswordHasherTests` -- hash/verify roundtrip, wrong password
  rejected, salting (same password hashes differently each call).
- `TenantContextTests` -- claim resolution, and that accessing tenant
  identity outside an authenticated request throws
  (`InvalidOperationException`) rather than silently returning an empty
  tenant id.

Integration tests:
- `AuthEndpointsTests` -- login success/failure, unauthenticated access
  to a protected endpoint returns 401, and (R4) exceeding the login rate
  limit within one window returns 429.
- `TenantIsolationTests` -- **the load-bearing test suite for ADR 0001**:
  a product created by one tenant's admin is not readable by ID, not
  returned by search, and not deletable, from another tenant's
  authenticated session.
- `ProductCrudTests` -- full create/update/delete happy path as Admin;
  Viewer forbidden from creating (403); duplicate SKU conflict (409);
  inventory adjustment below zero rejected (400).
- `SearchFacetsTests` -- category + price range filtering, attribute
  filtering (brand), category facet counts sum to total count (when no
  category filter is applied), in-stock filtering, and
  (independent-branch faceting) that the brand/category facets still
  list options excluded by the current filter (e.g. filtering to
  brand=Acme still lists "Pixel" in the brand facet).
- `InventoryEventStreamingTests` (ADR 0006) -- **the load-bearing test for
  the event-streaming addition**: creates a product and adjusts its
  inventory over real HTTP against the real API, and asserts (by polling
  MongoDB directly, since delivery is async and best-effort) that a real,
  independently-instantiated `InventoryProjectionConsumer` -- subscribed to
  a real NATS container, the same one the API published to -- lands both
  the `ProductCreated` and `InventoryAdjusted` events in its own
  `productInventoryProjection` collection with the correct final state.
  Nothing in this test is mocked: real API, real MongoDB, real NATS
  broker, real consumer code, real second MongoDB collection. Uses a new
  `NatsContainerFixture` (mirrors `MongoContainerFixture`) and an
  `EventStreaming collection` combining both.
- `SearchIndexingEventStreamingTests` (ADR 0007) -- **the load-bearing test
  for the search-indexer addition**: exercises the full product lifecycle
  (create, update, inventory adjust, delete) over real HTTP against the
  real API, and asserts (by polling the real Meilisearch index directly,
  since delivery is async and best-effort) that a real,
  independently-instantiated `SearchIndexingConsumer` -- subscribed to the
  same real NATS container the API published to -- lands all four events
  (`ProductCreated`, `ProductUpdated`, `InventoryAdjusted`,
  `ProductDeleted`) in Meilisearch with the correct final state,
  including that a deleted product actually stops existing in the index.
  Also proves the indexed document is genuinely *searchable* (a
  typo-tolerant query against Meilisearch's own search endpoint, not just
  a raw document-by-id lookup). Nothing in this test is mocked: real API,
  real MongoDB, real NATS broker, real Meilisearch instance, real consumer
  code. Uses a new `MeilisearchContainerFixture` (mirrors
  `NatsContainerFixture`, but wraps the generic Testcontainers
  `ContainerBuilder` directly since no official `Testcontainers.Meilisearch`
  module exists) and a `SearchIndexing collection` combining Mongo, NATS,
  and Meilisearch.

All integration tests run against the demo tenants/users seeded by
`DataSeeder` at startup (`admin@acme.test` / `viewer@acme.test` /
`admin@urbanthread.test`, password `Passw0rd!` for all three) plus
whatever products each test creates itself.

## Known sandbox limitation (read before assuming these are unverified)

**Sandboxes used to develop this repository cannot pull images from Docker
Hub** -- the environment's egress proxy returns 403 for
`production.cloudfront.docker.com` (Docker Hub's blob CDN), which blocks
`mongo:7.0`, `nats:2.10-alpine`, `getmeili/meilisearch` (ADR 0007), and the
`testcontainers/ryuk` resource-reaper image that Testcontainers needs for
all of them. `mcr.microsoft.com` (used for the .NET base images) *is*
reachable, so `dotnet build`/`dotnet format`/the unit test suite were all
verified directly in each such sandbox; the integration test suite
compiles cleanly (verified) but its tests (18 as of ADR 0007, up from 17)
fail purely at the container-pull step (`DockerImageNotFoundException`),
before any test body executes. This is an environment restriction, not a
code defect -- confirmed by checking the egress proxy's own status
endpoint, which logs `production.cloudfront.docker.com` as a policy
denial. See `ops.md`'s "Sandbox limitation" section (and its ADR-0006
addendum) for the exact restrictions hit each time, including ones beyond
Docker Hub.

GitHub Actions' `ubuntu-latest` runners have unrestricted internet access
and Docker pre-installed, so `ci.yml` runs this same integration suite
for real on every push/PR. The PR for this repository was the actual first
real-world verification of the original 16 tests, and CI was driven to
green as part of delivering that work (see `handoff.md`); the same is true
of `InventoryEventStreamingTests` for the ADR 0006 addition and of
`SearchIndexingEventStreamingTests` for this one -- both are real, and CI
is what actually proves they pass. The ADR 0007 session hit the identical
`DockerImageNotFoundException` at the `testcontainers/ryuk` pull step, for
all three fixtures (Mongo, NATS, and the new Meilisearch one) at once --
same root cause, same fallback (trust CI).

**A later session (security-hardening pass) confirmed the same root
cause via a different symptom**: `service docker start` failed there
(`ulimit: error setting limit: Operation not permitted`, from the init
script, not the daemon itself) -- but running `dockerd` directly in the
background started a working daemon (`docker info` succeeded). The pull
still failed exactly as described above: `docker pull hello-world` hit
`production.cloudfront.docker.com` and got `403 Forbidden` from the
proxy. So if `docker info` reports no daemon, don't stop there --
`service docker start` can fail on an environment quirk even when
`dockerd` itself works fine; try `dockerd &` (or your environment's
equivalent) before concluding Docker isn't usable at all. Either way,
once you confirm the CDN block (via a quick `docker pull hello-world` or
the proxy status endpoint), the fallback is the same: trust CI, and use
the DI-graph-only host-start check (documented in `CLAUDE.md`) plus unit
tests as the local substitute for anything auth/DI-wiring-shaped.

## Running the tests yourself

```bash
dotnet test tests/FlexCatalog.UnitTests/FlexCatalog.UnitTests.csproj
dotnet test tests/FlexCatalog.IntegrationTests/FlexCatalog.IntegrationTests.csproj  # requires Docker
```

## What's not covered (see backlog.md)

- Load/performance testing against a realistically-sized catalog.
- Contract/schema tests against the OpenAPI document.
