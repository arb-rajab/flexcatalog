# Testing

## Strategy

Two test projects, matching the two things worth testing differently:

- **`FlexCatalog.UnitTests`** -- pure logic, no external dependencies, no
  Docker required. Runs anywhere `dotnet test` runs, including this
  sandbox.
- **`FlexCatalog.IntegrationTests`** -- full HTTP-request-to-real-MongoDB
  behavior, via `WebApplicationFactory<Program>` and a MongoDB container
  started per test class by Testcontainers. Requires Docker.

## What's covered

Unit tests (44 tests at time of writing):
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
  `IProductRepository`.
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
  filtering (brand), category facet counts sum to total count, in-stock
  filtering.

All integration tests run against the demo tenants/users seeded by
`DataSeeder` at startup (`admin@acme.test` / `viewer@acme.test` /
`admin@urbanthread.test`, password `Passw0rd!` for all three) plus
whatever products each test creates itself.

## Known sandbox limitation (read before assuming these are unverified)

**This session's sandbox cannot pull images from Docker Hub** -- the
environment's egress proxy returns 403 for
`production.cloudfront.docker.com` (Docker Hub's blob CDN), which blocks
both `mongo:7.0` and the `testcontainers/ryuk` resource-reaper image that
Testcontainers needs. `mcr.microsoft.com` (used for the .NET base images)
*is* reachable, so the Docker image build and the unit tests were both
verified directly in this sandbox; the integration test suite compiles
cleanly (verified) but its 16 tests fail in *this* sandbox purely at the
container-pull step (`DockerImageNotFoundException` for the ryuk image),
before any test body executes. This is an environment restriction, not a
code defect -- confirmed by checking the egress proxy's own status
endpoint, which logs `production.cloudfront.docker.com` as a policy
denial.

GitHub Actions' `ubuntu-latest` runners have unrestricted internet access
and Docker pre-installed, so `ci.yml` runs this same integration suite
for real on every push/PR. The PR for this repository is the actual first
real-world verification of these tests, and CI was driven to green as
part of delivering this work (see `handoff.md` for the confirmed result).

**A later session (security-hardening pass) hit a stricter variant of
this**: that sandbox's `docker` CLI was present but there was no daemon
at all (`dockerd` refused to start -- `ulimit: error setting limit:
Operation not permitted`), not just an egress block on Docker Hub's CDN.
`docker info` (or trying `service docker start` and checking again) tells
you which case you're in faster than debugging a pull failure does. Either
way the fallback is the same: trust CI, and use the DI-graph-only
host-start check (documented in `CLAUDE.md`) plus unit tests as the
local substitute for anything auth/DI-wiring-shaped.

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
