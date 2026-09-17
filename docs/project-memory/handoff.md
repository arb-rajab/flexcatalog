# Handoff

Snapshot at initial delivery (PR #1, merged into `main`). See "Session 2"
below for the most recent work and the current "Remaining backlog"
section -- read that one, not this snapshot's now-stale references to
what was outstanding at the time.

## What shipped

A working multi-tenant product catalog and inventory API: JWT auth
(Admin/Viewer), full product CRUD + inventory adjustment, faceted search
via a MongoDB aggregation pipeline, tenant-isolated end to end, seeded
demo data, Docker + docker-compose, CI (format/build/unit/integration/
Docker image build), and the full `docs/project-memory` set including 4
ADRs.

## Real bugs CI caught that local checks in the build sandbox could not

The build sandbox used for this work could not pull Docker Hub images
(egress policy blocks Docker Hub's blob CDN -- see `testing.md`), so the
integration test suite (Testcontainers + real MongoDB, exercised via
`WebApplicationFactory<Program>`) could not run locally. It ran for the
first time as part of this PR's CI, on GitHub Actions' `ubuntu-latest`
runners, and it caught three real bugs that a DI-graph-only local check
could not have caught:

1. **`DataSeeder` (a Singleton `IHostedService`) consumed Scoped
   repositories.** ASP.NET Core's service-provider validation only runs
   when the host actually starts in `Development` (e.g. under
   `WebApplicationFactory`) -- `dotnet build`/`dotnet run` never
   triggered it. Fixed by making `ITenantRepository`/`IUserRepository`
   Singleton (they hold no per-request state, unlike `ProductRepository`,
   which legitimately needs the per-request `ITenantContext` and stayed
   Scoped).
2. **JWT validation used a stale, pre-`Build()` secret.** `Program.cs`
   captured `Jwt:Secret` into a local variable before `builder.Build()`
   and closed over it inside the `AddJwtBearer` delegate. Harmless at
   real startup; broken under `WebApplicationFactory`, whose config
   overrides are only merged in at `Build()` time -- so validation kept
   checking signatures against the pre-override secret while token
   issuance (which resolves `IOptions<JwtOptions>` lazily, after
   `Build()`) correctly used the overridden one. Every login succeeded;
   every subsequent authenticated call failed with 401. Fixed by reading
   the secret inside the lazy options delegate instead.
3. **The `Admin` authorization policy always returned 403, for every
   role.** `JwtSecurityTokenHandler` silently remaps well-known short
   claim types on validation (`"role"` -> `ClaimTypes.Role`, `"sub"` ->
   `ClaimTypes.NameIdentifier`) unless `JwtBearerOptions.MapInboundClaims`
   is explicitly set to `false`. The `Admin` policy's
   `RequireClaim("role", "Admin")` checked the literal short claim type,
   which no longer existed on the validated principal. Fixed with
   `options.MapInboundClaims = false`.

All three were root-caused, fixed, and pinned with regression tests
(`ProductRepository`/DI: verified by starting the host locally against an
unreachable Mongo endpoint to confirm the DI graph builds;
`JwtSecretConfigurationTests`, `InboundClaimMappingTests`: unit tests
exercising the exact validation mechanism) in the same PR, then confirmed
fixed by a subsequent green CI run -- not asserted from local
verification alone. See the commit history on this PR for the full
diagnosis-and-fix trail.

## Tech stack

ASP.NET Core Minimal APIs on .NET 10 (SDK 10.0.112 at time of writing),
MongoDB.Driver 3.11.2 against MongoDB 7.0, JWT bearer auth (self-issued,
HS256), BCrypt.Net-Next for password hashing, built-in
`Microsoft.AspNetCore.OpenApi` + Scalar for API docs (not Swashbuckle --
see `architecture.md` for why). xUnit v2 for both test projects;
Testcontainers.MongoDb + `WebApplicationFactory<Program>` for integration
tests.

## Key architecture decisions (full reasoning in `decisions/000{1-4}`)

1. **Multi-tenant isolation**: shared collection + `tenantId` field,
   enforced structurally by `ProductRepository` (the only class allowed
   to touch the `products` collection), tenant identity taken only from
   the validated JWT.
2. **MongoDB schema/indexing**: polymorphic per-category
   `ProductAttributes` subtypes (Electronics/Apparel/Books), camelCase
   Bson naming convention matching JSON, tenant-first compound indexes
   including a compound wildcard index over `attributes`.
3. **JWT auth**: self-issued HS256, globally-unique usernames (tenant
   determined by account, not chosen at login), Admin/Viewer roles.
4. **Faceted search**: one `$match` + `$facet` aggregation pipeline per
   search request; documented, deliberate simplifications (facets net of
   the full filter, not independent-per-facet; a fixed short facetable
   field list) rather than silently shipping partial sophistication.

## Endpoints

- `POST /api/auth/login` (anonymous)
- `GET /health` (anonymous, pings MongoDB)
- `GET /api/categories` (authenticated)
- `GET /api/products/{id}`, `POST /api/products/search` (authenticated,
  any role)
- `POST /api/products`, `PUT /api/products/{id}`,
  `DELETE /api/products/{id}`, `POST /api/products/{id}/inventory/adjust`
  (Admin only)

## Tests

38 unit tests (no external dependencies -- domain validation, the search
aggregation pipeline's pure query-building logic, product service business
rules via Moq, JWT issuance/claims, password hashing, tenant-context claim
resolution, and the two JWT-validation regression tests above). 16
integration tests (Testcontainers + real MongoDB, over real HTTP via
`WebApplicationFactory`): auth flow, **tenant isolation** (the load-bearing
suite for ADR 0001 -- cross-tenant read/search/delete all proven blocked),
full product CRUD + role enforcement, faceted search filters and facet
counts. All 54 tests pass in CI as of the merged commit.

## CI status (per check, on the merged commit)

- `build-and-test`: **passing** -- format check (`dotnet format
  --verify-no-changes`), Release build (0 warnings), 38 unit tests, 16
  integration tests, all green.
- `docker-build`: **passing** -- multi-stage Docker image builds clean.

## Docker status

- Multi-stage, non-root Dockerfile: **build-verified**, both locally
  (using a temporary, non-committed proxy/CA workaround needed only
  because of this specific sandbox's network policy -- see `ops.md`) and
  by CI's `docker-build` job on unrestricted GitHub Actions runners.
- `docker compose up` (API + MongoDB together): **not run end-to-end** in
  the development sandbox (Docker Hub-hosted `mongo:7.0` was
  unreachable there); the equivalent -- API talking to a real MongoDB
  over the network -- **is** exercised by every integration test in CI,
  which is the stronger signal. Documented as an explicit gap rather than
  claimed as verified; see `ops.md`.

## Dependabot status: **unverifiable-explain-why**

This session's GitHub MCP toolset has no Dependabot-alerts-specific tool
(no `list_dependabot_alerts` or equivalent), and no generic authenticated
GitHub REST/GraphQL API access was available to query
`/repos/{owner}/{repo}/dependabot/alerts` directly. I could not enumerate
Dependabot alerts for this repository and am not claiming "none found" --
that would be a guess, not a verified result. What I did instead:
resolved all NuGet package versions to whatever was latest-stable at
restore time (`MongoDB.Driver` 3.11.2, `Microsoft.AspNetCore
.Authentication.JwtBearer`/`Microsoft.AspNetCore.OpenApi` 10.0.12,
`BCrypt.Net-Next` 4.2.0, `Scalar.AspNetCore` 2.17.3, test packages
similarly current), which reduces the likelihood of picking up an
already-known-vulnerable version, but is not equivalent to a verified
clean Dependabot scan. **Recommended next step**: whoever has repository
admin access should check the Security tab / Dependabot alerts page on
GitHub directly, or grant a session the necessary API scope.

## docs/project-memory completeness

All required documents present: brief, requirements, architecture,
security, testing, ops, 4 ADRs (`decisions/0001`-`0004`), risk register,
backlog, handoff (this file), release notes, retirement plan.

## PR and merge

- PR: [arb-rajab/flexcatalog#1](https://github.com/arb-rajab/flexcatalog/pull/1)
- Bootstrap note: the repository had zero commits when this work began;
  `main` was initialized with a minimal placeholder commit (verified via
  `list_branches` returning only the feature branch, and a direct 409
  "Git Repository is empty" from the GitHub API) so this PR could carry a
  real, reviewable diff instead of GitHub refusing a diff-less PR against
  a branch that didn't yet exist. Full detail in the PR description.
- Merge: performed via the GitHub API (`merge_pull_request`) after CI
  went green on commit `c21aa4c` (workflow run
  [35085333704](https://github.com/arb-rajab/flexcatalog/actions/runs/35085333704),
  conclusion `success`) and `pull_request_read` reported
  `mergeable_state: "clean"`.

## Session 2 (2026-09-17): Security hardening + faceted navigation

Backlog cleanup session, worked in the priority order set out in the
session brief: security hardening first, faceted navigation second,
everything else only if time allowed.

### Completed

1. **Refuse-to-start on placeholder JWT secret** (closes `risk.md` R2).
   `Auth/JwtSecretGuard.EnsureNotPlaceholder`, called from `Program.cs`
   right where the existing empty-secret check already was; throws only
   when both "running in `Production`" and "secret equals the exact
   known `appsettings.Development.json` placeholder string" are true.
   Extracted as a static, host-independent method specifically so it's
   unit-testable (`JwtSecretGuardTests`, 4 tests) without needing a full
   ASP.NET host -- same pattern this repo already uses for
   `ProductSearchService.BuildStructuredMatchDocument`.
2. **Login rate limiting** (closes `risk.md` R4). `POST /api/auth/login`
   now enforces a fixed-window limit via ASP.NET Core's built-in rate
   limiter: **5 attempts / 60 seconds, partitioned per client IP, HTTP
   429 with no queueing past the limit**
   (`Infrastructure/LoginRateLimiting`). Pinned by a unit test exercising
   the same `FixedWindowRateLimiter` configuration directly
   (`LoginRateLimitingTests`) and an integration test proving the actual
   endpoint returns 429 after the 6th rapid attempt. Known limitation,
   documented in `security.md`: per-IP partitioning doesn't slow a
   distributed (many-IP) attack.
3. **JWT revocation / refresh-token model**: evaluated and explicitly
   **re-affirmed as deferred**, not half-shipped -- see `backlog.md` item
   3 for what "fully done" would require and the recommendation to pick
   *one* of (revocation-only deny-list) or (full refresh-token flow) next
   time, not both at once.
4. **Independent-branch faceted navigation** (ADR 0004, closes `risk.md`
   R7 for the category and attribute facets). The category facet and
   each attribute facet (brand/sizes/colors/author) now compute counts
   net of every filter except their own -- filtering to one brand still
   shows the others as options. Required restructuring the aggregation
   pipeline (`$text` moved to its own mandatory top-level `$match`,
   since MongoDB disallows `$text` inside a `$facet` sub-pipeline; every
   other filter moved into per-branch `$match` stages via the new
   `BuildStructuredMatchDocument(excludeCategory:, excludeAttributeKey:)`).
   The price-range facet is a deliberate, documented exception, still net
   of the full filter. Pinned by new/updated
   `ProductSearchServiceQueryBuildingTests` (pipeline shape, no DB) and
   two new `SearchFacetsTests` (end-to-end against real Mongo).
5. **Design doc for the two lowest-priority items** (structured
   logging/tracing, database-per-tenant scaling), rather than a rushed
   partial implementation of either: **ADR 0005**. Concrete proposed
   approach and trigger conditions for both; nothing implemented.

### Verification performed this session

- `dotnet build FlexCatalog.slnx --configuration Release`: clean, 0
  warnings, after every change (this session installed .NET SDK 10.0.112
  via `apt-get install dotnet-sdk-10.0` -- this remote-execution sandbox
  didn't have `dotnet` preinstalled at session start, unlike the sandbox
  `CLAUDE.md`'s existing notes assume; worth knowing for the next session
  in an environment like this one).
- `dotnet format FlexCatalog.slnx --verify-no-changes --severity warn`:
  clean after every change.
- `dotnet test tests/FlexCatalog.UnitTests`: 51/51 passing (was 38 at
  last handoff; +13 this session: 4 `JwtSecretGuardTests`, 2
  `LoginRateLimitingTests`, 7 new/rewritten
  `ProductSearchServiceQueryBuildingTests`).
- DI-graph-only host-start check (`ASPNETCORE_ENVIRONMENT=Development`,
  `Mongo__ConnectionString=mongodb://localhost:1/`, per `CLAUDE.md`): ran
  after the auth-adjacent changes (items 1-2); host started cleanly and
  failed only on the expected Mongo connection timeout, confirming no DI
  lifetime regression, before moving on to faceted navigation.
- **Integration suite (Testcontainers, 19 tests -- 16 pre-existing + 3
  new this session) was not run in this sandbox.** Docker Hub's blob CDN
  (`production.cloudfront.docker.com`) is proxy-blocked here, confirmed
  directly (`docker pull hello-world` -> `403 Forbidden`), same root
  cause `testing.md` already documents -- notably, `dockerd` itself
  *could* be started manually in this particular sandbox (`service
  docker start` failed on an unrelated `ulimit` permission error, but
  running `dockerd` directly worked and produced a healthy daemon); the
  CDN block is what actually stops the pull, not the daemon. This is
  trusted to CI per this project's established convention -- **no PR was
  opened this session** (not asked for), so CI has not yet run against
  this branch; the next step for whoever picks this up is to open a PR
  (or ask this session/a follow-up to) so CI actually verifies the
  integration suite, particularly the two new independent-branch
  faceting tests and the new rate-limit test, none of which have been
  confirmed against a real MongoDB yet.
- All work is committed and pushed to
  `claude/flexcatalog-security-hardening-rfb7or` (three commits: security
  hardening, faceted navigation, ADR 0005).

## Remaining backlog -- flagged, current as of Session 2 (see `backlog.md` for full detail)

**Not yet verified by CI** (highest-priority follow-up, not a design
gap): the 3 tests added this session need a real green CI run once a PR
exists -- see "Verification performed this session" above.

**Still open, in priority order**:
1. JWT revocation / refresh-token model (`backlog.md` item 3) --
   deliberately deferred, not started. Pick one design (deny-list or
   refresh-token flow), not both, next time.
2. Structured logging + OpenTelemetry tracing (`backlog.md` item 8) --
   design proposed in ADR 0005 Part 1, not implemented; needs real
   infrastructure to verify against, which this sandbox lacked.
3. Database-per-tenant migration path (`backlog.md` item 6) -- design
   proposed in ADR 0005 Part 2 (trigger conditions + per-tenant migration
   approach), not implemented; has an explicit dependency on (2) for
   noisy-neighbor *measurement* before it's worth starting.
4. Configurable/derived facet field list (`backlog.md` item 5) -- needs a
   bounded-cardinality safety check first; not attempted.
5. Everything else in `backlog.md` (platform-admin cross-tenant
   reporting, seed-data reset tooling, product-feature scope items,
   load/perf testing, OpenAPI contract tests) -- untouched this session,
   unchanged priority.
