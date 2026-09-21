# Operations

## Running locally

```bash
# Option A: docker compose (API + MongoDB + NATS + Meilisearch + the
# inventory projector + the search indexer)
export FLEXCATALOG_JWT_SECRET=$(openssl rand -base64 48)
export FLEXCATALOG_MEILI_MASTER_KEY=$(openssl rand -base64 24)
docker compose up --build
# API: http://localhost:8080  (Scalar docs at /scalar in Development)
# MongoDB: localhost:27017
# NATS: localhost:4222 (monitoring endpoint on :8222 inside the container)
# Meilisearch: http://localhost:7700

# Option B: dotnet directly, against a MongoDB (and, for the event-streaming
# path, a NATS server, and for search indexing, a Meilisearch instance) you
# run yourself
dotnet run --project src/FlexCatalog.Api
dotnet run --project src/FlexCatalog.InventoryProjector
dotnet run --project src/FlexCatalog.SearchIndexer
```

`ASPNETCORE_ENVIRONMENT=Development` (the docker-compose default) makes
the API seed two demo tenants, three demo users, and six demo products at
startup -- see `DataSeeder` and `testing.md` for the seeded credentials.
This is deliberate for a demo/portfolio repo; a real deployment should set
`ASPNETCORE_ENVIRONMENT=Production`, which disables seeding and the
Scalar UI.

## Configuration reference

| Setting | Env var | Purpose |
|---|---|---|
| `Mongo:ConnectionString` | `Mongo__ConnectionString` | MongoDB connection string |
| `Mongo:DatabaseName` | `Mongo__DatabaseName` | Database name |
| `Jwt:Secret` | `Jwt__Secret` | HS256 signing secret; **required**, app fails to start if empty |
| `Jwt:Issuer` / `Jwt:Audience` | `Jwt__Issuer` / `Jwt__Audience` | JWT validation |
| `Jwt:ExpiryMinutes` | `Jwt__ExpiryMinutes` | Token lifetime |
| `Nats:Url` | `Nats__Url` | NATS server URL (`FlexCatalog.Api`, `FlexCatalog.InventoryProjector`, and `FlexCatalog.SearchIndexer`, ADR 0006/0007). Unreachable is non-fatal for the API -- see "What's deliberately not here" in `architecture.md`. |
| `Nats:SubjectPrefix` | `Nats__SubjectPrefix` | Subject prefix events are published/subscribed under; defaults to `flexcatalog.events` and normally left alone. |
| `Meilisearch:Url` | `Meilisearch__Url` | Meilisearch instance URL (`FlexCatalog.Api` and `FlexCatalog.SearchIndexer`, ADR 0007). |
| `Meilisearch:ApiKey` | `Meilisearch__ApiKey` | Meilisearch API key. `SearchIndexer` needs the master key (it creates/configures the index); `Api` only needs a search-scoped key in production -- see `Search/MeilisearchOptions.cs`. |
| `Meilisearch:IndexName` | `Meilisearch__IndexName` | Index name both processes agree on; defaults to `products` and normally left alone. |

## Health

`GET /health` (unauthenticated) pings MongoDB (`{ping: 1}` against the
`admin` database) and returns 200/`healthy` or 503/`unhealthy`. Suitable
as a container orchestrator liveness/readiness probe.

## Deployment shape this was built for

A stateless API container, a MongoDB instance (managed Atlas cluster, or
self-hosted replica set), and, since ADR 0006, a NATS server plus the
`FlexCatalog.InventoryProjector` worker as a fourth, independently
deployable/scalable process -- see the Dockerfile, `Dockerfile.projector`,
and docker-compose.yml. Since ADR 0007, a fifth and sixth: a Meilisearch
instance and the `FlexCatalog.SearchIndexer` worker (`Dockerfile.searchindexer`).
The API, the projector, and the search indexer all hold no local state;
horizontal scaling is "run more copies of the container," with MongoDB and
Meilisearch as the shared state each depends on respectively. Neither the
projector nor the search indexer is on the API's request path (ADR 0006 /
0007) -- either can be down, slow, or scaled independently without
affecting `POST /api/products` or any other endpoint's availability or
latency. TLS termination happens upstream of the container
(`architecture.md` / `security.md`).

## Indexes at startup

`MongoIndexInitializer` (an `IHostedService`) creates all indexes
idempotently on every startup (`CreateOneAsync`/`CreateManyAsync`, which
no-op against an already-equivalent index). There is no separate
migration step to run before deploying a new version -- the index set is
part of the application, not a change managed out-of-band. If an index
definition changes in a way MongoDB considers incompatible with the
existing index (e.g. changing a non-unique index to unique on data that
violates uniqueness), that will surface as a startup error, not a silent
skip -- worth knowing before assuming a deploy will always sail through.

## Known operational limitations (see risk.md for the full list)

- No token revocation -- see `security.md`.
- No rate limiting anywhere (login included).
- No structured/centralized logging shipped by default (standard ASP.NET
  Core console logging only) -- fine for a demo, would need a sink
  (e.g. to a log aggregator) for real operation.
- No metrics/tracing (OpenTelemetry) wired up.
- Single MongoDB database for all tenants (by design, ADR 0001) -- see
  `risk.md` for the scaling path if a tenant's data volume or isolation
  requirements outgrow that.

## Sandbox limitation encountered while building this

The build sandbox used to develop this repository could not pull images
from Docker Hub (egress policy blocks `production.cloudfront.docker.com`,
Docker Hub's blob CDN) — `mcr.microsoft.com` (the .NET base images) *was*
reachable. Consequences, spelled out precisely so nothing is silently
assumed to have been verified when it wasn't:

- The Docker image build (`docker build .`) **was verified successfully**
  in the sandbox, using a temporary local-only variant of the Dockerfile
  that trusted the sandbox's egress-proxy CA certificate purely to reach
  NuGet through the proxy during `dotnet restore` -- that workaround is
  not part of the committed Dockerfile and is not needed in any normal
  network (including GitHub Actions).
- `docker compose up` (which additionally needs `mongo:7.0` from Docker
  Hub) **was not run end-to-end** in the sandbox.
- The integration test suite **was not executed** in the sandbox (Docker
  Hub-hosted images required by Testcontainers were unreachable); see
  `testing.md` for the exact failure mode and why it's an environment
  limitation, not a code defect. It **was and is being verified for real
  by GitHub Actions CI**, which has unrestricted internet access.

### Addendum (2026-09-19, ADR 0006 event-streaming work)

The sandbox used for this addition had the same Docker Hub restriction
(confirmed again: `docker pull mongo:7.0` and `docker pull nats:2.10-alpine`
both fail identically), plus two more restrictions the note above didn't
need to cover: no `.NET SDK` was pre-installed (worked around via
`apt-get install dotnet-sdk-10.0`, since Ubuntu's own package archive isn't
behind the same policy as Docker Hub or the official dotnet-install CDN),
and this time the CA-trust workaround that verified `docker build .` in the
original session **did not work** -- the container could not reach the
sandbox's egress-proxy loopback address even with `--network host` (this
sandbox's Docker daemon doesn't share the outer host's network namespace
the way that flag normally implies). So for this addition specifically,
neither `docker build` (API or `Dockerfile.projector`, since both now
`dotnet restore` inside the container) nor the integration suite could be
run locally -- both are, as before, verified by CI. `dotnet build`/`dotnet
format`/the unit test suite all ran and passed locally, same as always.
See `CLAUDE.md` for the generalized version of this finding.

### Addendum (2026-09-21, ADR 0007 search-indexer work)

This sandbox had no `dotnet` at all pre-installed (unlike CLAUDE.md's
description of a typical sandbox) -- `apt-get install -y dotnet-sdk-10.0`
after `apt-get update` installed .NET 10.0.112 cleanly (Ubuntu's own
archive, not gated the same way Docker Hub or `builds.dotnet.microsoft.com`
are; the latter returned a 403 from the egress proxy when tried directly).
Once installed, `dotnet build`/`dotnet test` (unit) both ran and passed
locally as normal. Docker: `service docker start` failed the same way the
2026-09-19 addendum describes (`ulimit: error setting limit: Operation not
permitted`), but `sudo dockerd &` started a working daemon again, same
workaround. `docker pull hello-world` confirmed the same
`production.cloudfront.docker.com` 403 as every prior session. With a
working daemon, the three integration-test container fixtures (Mongo,
NATS, and the new Meilisearch one) were actually exercised this time
(`dotnet test --filter SearchIndexingEventStreamingTests`) and all three
failed identically and immediately at the `testcontainers/ryuk` pull step
(`DockerImageNotFoundException`) -- confirming the new `MeilisearchContainerFixture`
fails for the same environment reason as the existing fixtures, not a
defect in it, without needing to reason about it indirectly. As before:
trust CI for the integration suite.
