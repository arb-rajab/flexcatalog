# Testing

## Strategy

Two test projects, matching the two things worth testing differently:

- **`FlexCatalog.UnitTests`** -- pure logic, no external dependencies, no
  Docker required. Runs anywhere `dotnet test` runs, including this
  sandbox.
- **`FlexCatalog.IntegrationTests`** -- full HTTP-request-to-real-MongoDB
  behavior, via `WebApplicationFactory<Program>` and a MongoDB container
  started per test class by Testcontainers. Requires Docker. Since ADR
  0005, one suite in here (`InventoryEventStreamingTests`) also spins up a
  real NATS container and a real, directly-instantiated
  `FlexCatalog.InventoryProjector.InventoryProjectionConsumer` to prove the
  event-streaming path end to end -- see below.

## What's covered

Unit tests (42 tests at time of writing):
- `ProductValidationTests` -- category/attribute-shape matching
  invariant (ADR 0002).
- `ProductSearchServiceQueryBuildingTests` -- the aggregation
  pipeline-building logic (`BuildMatchDocument`, `BuildFacetStage`)
  exercised as pure BSON-document construction: category/price/stock
  filters, text search, attribute filter `$in` clauses, **rejection of
  unsafe attribute filter keys** (operator-injection prevention, ADR
  0004), pagination math, sort-order mapping. These are exposed
  `internal` + `InternalsVisibleTo` specifically so this logic is testable
  without a live database.
- `ProductServiceTests` -- create/update/delete/inventory-adjust business
  rules (duplicate SKU -> 409, mismatched attributes -> 400, negative
  inventory -> 400, not-found -> 404), using Moq against
  `IProductRepository`; since ADR 0005, also asserts `CreateAsync`/
  `AdjustInventoryAsync` publish the right domain event on success and
  publish nothing when validation rejects the request first, using Moq
  against `IDomainEventPublisher` -- no NATS needed for this part, only the
  end-to-end integration test below touches a real broker.
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
  to a protected endpoint returns 401.
- `TenantIsolationTests` -- **the load-bearing test suite for ADR 0001**:
  a product created by one tenant's admin is not readable by ID, not
  returned by search, and not deletable, from another tenant's
  authenticated session.
- `ProductCrudTests` -- full create/update/delete happy path as Admin;
  Viewer forbidden from creating (403); duplicate SKU conflict (409);
  inventory adjustment below zero rejected (400).
- `SearchFacetsTests` -- category + price range filtering, attribute
  filtering (brand), category facet counts sum to total count, in-stock
  filtering.
- `InventoryEventStreamingTests` (ADR 0005) -- **the load-bearing test for
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

All integration tests run against the demo tenants/users seeded by
`DataSeeder` at startup (`admin@acme.test` / `viewer@acme.test` /
`admin@urbanthread.test`, password `Passw0rd!` for all three) plus
whatever products each test creates itself.

## Known sandbox limitation (read before assuming these are unverified)

**Sandboxes used to develop this repository cannot pull images from Docker
Hub** -- the environment's egress proxy returns 403 for
`production.cloudfront.docker.com` (Docker Hub's blob CDN), which blocks
`mongo:7.0`, `nats:2.10-alpine`, and the `testcontainers/ryuk`
resource-reaper image that Testcontainers needs for all of them.
`mcr.microsoft.com` (used for the .NET base images) *is* reachable, so
`dotnet build`/`dotnet format`/the unit test suite were all verified
directly in each such sandbox; the integration test suite compiles cleanly
(verified) but its tests (17 as of ADR 0005, up from 16) fail purely at the
container-pull step (`DockerImageNotFoundException`), before any test body
executes. This is an environment restriction, not a code defect --
confirmed both times by checking the egress proxy's own status endpoint,
which logs `production.cloudfront.docker.com` as a policy denial. See
`ops.md`'s "Sandbox limitation" section (and its ADR-0005 addendum) for the
exact restrictions hit each time, including ones beyond Docker Hub.

GitHub Actions' `ubuntu-latest` runners have unrestricted internet access
and Docker pre-installed, so `ci.yml` runs this same integration suite
for real on every push/PR. The PR for this repository was the actual first
real-world verification of the original 16 tests, and CI was driven to
green as part of delivering that work (see `handoff.md`); the same is true
of `InventoryEventStreamingTests` for this addition -- it's new, real, and
CI is what actually proves it passes.

## Running the tests yourself

```bash
dotnet test tests/FlexCatalog.UnitTests/FlexCatalog.UnitTests.csproj
dotnet test tests/FlexCatalog.IntegrationTests/FlexCatalog.IntegrationTests.csproj  # requires Docker
```

## What's not covered (see backlog.md)

- Load/performance testing against a realistically-sized catalog.
- The independent-branch faceting behavior described as a limitation in
  ADR 0004 -- because it isn't implemented, there's nothing to test yet.
- Contract/schema tests against the OpenAPI document.
