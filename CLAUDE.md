# CLAUDE.md

Repo-specific notes for future Claude Code sessions working on FlexCatalog.
Concrete, not generic advice -- update this file when you learn something
else that would have saved a round trip.

## Sandbox network gotcha (saves a full debug cycle)

If you're in a sandboxed environment with an egress proxy: `mcr.microsoft.com`
(the .NET base images) is typically reachable, but **Docker Hub's blob CDN
(`production.cloudfront.docker.com`) is commonly blocked** by egress policy.
That means `docker pull mongo:*` and `testcontainers/ryuk` (which
`FlexCatalog.IntegrationTests` needs) will fail, but `docker build` of
*this repo's own Dockerfile* will succeed, since its base images are all
`mcr.microsoft.com`. Don't burn time debugging "why won't Testcontainers
pull" in such an environment -- check `curl -sS
"$HTTPS_PROXY/__agentproxy/status"` (or your environment's equivalent)
for a policy denial on the Docker Hub CDN host first. The integration
suite is verified by CI (GitHub Actions has unrestricted internet), not
by local runs, in that situation -- see `docs/project-memory/testing.md`.

**Also don't burn time on `docker build` itself if it needs to restore
NuGet packages.** Pulling `mcr.microsoft.com/dotnet/sdk:*` succeeds (that's
a host-side `docker pull`), but any `RUN dotnet restore`/`dotnet publish`
step executes *inside* the container's own network namespace, which this
sandbox's egress proxy does not extend into -- setting `HTTPS_PROXY`
inside the `Dockerfile` and even building with `--network host` still
gets "connection refused" reaching the proxy's loopback address, because
this sandbox's Docker daemon doesn't share the outer host's network stack
the way `--network host` normally implies. There is no local workaround
for this one (unlike the Docker Hub case above, which is a real, checkable
policy denial) -- multi-project solutions especially (any `Dockerfile`
that `COPY`s more than one `.csproj` before restoring) are effectively
Docker-build-unverifiable in this class of sandbox. Trust CI's
`docker-build` job here too.

## Don't re-litigate these three fixed bugs

If you're touching `Program.cs`'s auth/DI wiring, these three were root-
caused once already (see `docs/project-memory/handoff.md` for full
detail) -- don't rediscover them from scratch:

1. Any `IHostedService` registered as Singleton (via `AddHostedService`)
   cannot depend on a Scoped service. `dotnet build`/`dotnet run` will
   NOT catch this -- only a host that actually starts with scope
   validation on (e.g. `WebApplicationFactory` in Development) catches
   it. If you add a new hosted service, check what its dependencies'
   lifetimes are before assuming a clean build means it's wired
   correctly.
2. Never capture a config value (`builder.Configuration[...]`) into a
   local variable *before* `builder.Build()` if that value might be
   overridden by a test host (`WebApplicationFactory.ConfigureWebHost`
   overrides are merged in at `Build()` time, not before). Read
   configuration lazily, inside an options delegate
   (`.AddJwtBearer(options => { /* read here */ })`), not eagerly at the
   top level.
3. `JwtBearerOptions.MapInboundClaims` defaults to `true`, which silently
   remaps short claim types (`"role"`, `"sub"`) to long-form
   `ClaimTypes.*` URIs on validation. This project issues and reads the
   short names directly (`FlexClaimTypes`), so `MapInboundClaims = false`
   is required and is already set in `Program.cs` -- don't remove it.

## Fast local verification loop

- If `dotnet` isn't on `PATH` at all (a fresh sandbox with no SDK
  pre-installed), don't reach for the `dotnet-install.sh` script --
  `builds.dotnet.microsoft.com` (where it downloads the actual SDK archive
  from) is commonly policy-denied, same class of block as the Docker Hub
  CDN above. `apt-get install -y dotnet-sdk-10.0` works instead (Ubuntu's
  own archive mirrors the SDK and isn't blocked) and gets you the same
  version CI uses.
- `dotnet build FlexCatalog.slnx --configuration Release` -- whole
  solution builds in ~3-5s. Do this before every push; it's cheap.
- `dotnet format FlexCatalog.slnx --verify-no-changes --severity warn` --
  ~instant, deterministic. Run before pushing to avoid a wasted CI
  round-trip on a formatting-only failure.
- `dotnet test tests/FlexCatalog.UnitTests/...` -- ~2s, no external
  dependencies (no Docker/Mongo needed). Always safe and fast to run
  locally.
- `dotnet test tests/FlexCatalog.IntegrationTests/...` needs Docker with
  real internet access to pull `mongo:7.0` and `testcontainers/ryuk`.
  Only attempt this locally if you've confirmed Docker Hub is reachable
  (see the gotcha above) -- otherwise trust CI's `build-and-test` job,
  which runs the same command on an unrestricted runner.
- A DI-graph problem (lifetime mismatches) can be caught **without**
  Docker or MongoDB: run the published app against a deliberately
  unreachable Mongo connection string
  (`Mongo__ConnectionString=mongodb://localhost:1/`) with
  `ASPNETCORE_ENVIRONMENT=Development`. `builder.Build()` will throw
  immediately if the DI graph is invalid, before ever attempting to
  connect to Mongo -- this is how bug #1 above was confirmed fixed
  without needing a real database.

## Where to extend, without needing a live database

`ProductSearchService.BuildMatchDocument` and `.BuildFacetStage` are
`internal` (via `InternalsVisibleTo`) specifically so the aggregation
pipeline's *shape* is unit-testable as pure `BsonDocument` construction.
If you add a new search filter or facet, extend
`ProductSearchServiceQueryBuildingTests` first -- you don't need
MongoDB running to verify the pipeline is built correctly, only to verify
it executes correctly (that part is the integration suite's job).

## Don't re-read these every session

- `docs/project-memory/decisions/000{1-4}-*.md` are the architecture
  reasoning and rarely change; skim `architecture.md`'s summary instead
  of re-reading all four ADRs in full unless you're specifically changing
  tenant isolation, schema/indexing, auth, or search.
- `docs/project-memory/handoff.md` documents bugs and decisions already
  fixed/made at initial delivery -- read it once, don't re-derive the
  same history from the git log every session.
