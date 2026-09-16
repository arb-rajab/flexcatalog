# FlexCatalog

A multi-tenant product catalog and inventory API built on ASP.NET Core
(.NET 10) and MongoDB. Portfolio skill-demo focused on .NET + NoSQL:
genuinely different attribute shapes per product category, structural
tenant isolation, and faceted search via a MongoDB aggregation pipeline.

See [`docs/project-memory/brief.md`](docs/project-memory/brief.md) for
what this is and why, and
[`docs/project-memory/decisions/`](docs/project-memory/decisions/) for the
architecture decision records (multi-tenant isolation, schema/indexing,
auth, faceted search).

## Quick start

```bash
export FLEXCATALOG_JWT_SECRET=$(openssl rand -base64 48)
docker compose up --build
```

API at `http://localhost:8080`. Interactive docs at `/scalar` (in
Development, which is docker-compose's default). Demo data (two tenants,
three users, six products) is seeded automatically on first startup --
see [`docs/project-memory/ops.md`](docs/project-memory/ops.md) for
credentials and details.

```bash
curl -X POST http://localhost:8080/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin@acme.test","password":"Passw0rd!"}'
```

## Running tests

```bash
dotnet test tests/FlexCatalog.UnitTests/FlexCatalog.UnitTests.csproj
dotnet test tests/FlexCatalog.IntegrationTests/FlexCatalog.IntegrationTests.csproj  # needs Docker
```

## Project layout

```
src/FlexCatalog.Api/           The API (see architecture.md for layering)
tests/FlexCatalog.UnitTests/   No external dependencies
tests/FlexCatalog.IntegrationTests/  Testcontainers + real MongoDB
docs/project-memory/           Brief, requirements, architecture, security,
                                testing, ops, ADRs, risk, backlog, handoff
```

## Docs index

- [Brief](docs/project-memory/brief.md)
- [Requirements](docs/project-memory/requirements.md)
- [Architecture](docs/project-memory/architecture.md)
- [Security](docs/project-memory/security.md)
- [Testing](docs/project-memory/testing.md)
- [Operations](docs/project-memory/ops.md)
- [Decisions / ADRs](docs/project-memory/decisions/)
- [Risk register](docs/project-memory/risk.md)
- [Backlog](docs/project-memory/backlog.md)
- [Handoff](docs/project-memory/handoff.md)
- [Release notes](docs/project-memory/release-notes.md)
- [Retirement plan](docs/project-memory/retirement-plan.md)
