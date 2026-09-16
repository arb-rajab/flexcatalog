# Operations

## Running locally

```bash
# Option A: docker compose (API + MongoDB)
export FLEXCATALOG_JWT_SECRET=$(openssl rand -base64 48)
docker compose up --build
# API: http://localhost:8080  (Scalar docs at /scalar in Development)
# MongoDB: localhost:27017

# Option B: dotnet directly, against a MongoDB you run yourself
dotnet run --project src/FlexCatalog.Api
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

## Health

`GET /health` (unauthenticated) pings MongoDB (`{ping: 1}` against the
`admin` database) and returns 200/`healthy` or 503/`unhealthy`. Suitable
as a container orchestrator liveness/readiness probe.

## Deployment shape this was built for

A single stateless API container plus a MongoDB instance (managed Atlas
cluster, or self-hosted replica set) -- see the Dockerfile and
docker-compose.yml. The API holds no local state; horizontal scaling is
"run more copies of the container," with MongoDB as the sole shared
state. TLS termination happens upstream of the container (`architecture.md`
/ `security.md`).

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
