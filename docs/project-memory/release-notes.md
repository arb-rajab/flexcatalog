# Release Notes

## v0.1.0 -- Initial release (2026-09-16)

First working version of FlexCatalog: a multi-tenant product catalog and
inventory API on ASP.NET Core (.NET 10) + MongoDB.

### Added
- Multi-tenant product catalog across three categories (Electronics,
  Apparel, Books) with genuinely different, polymorphically-serialized
  attribute shapes per category (ADR 0002).
- Structural, repository-enforced tenant isolation (ADR 0001) -- tenant
  identity derived solely from the JWT, never from client input.
- JWT authentication (self-issued, HS256), Admin/Viewer roles (ADR 0003).
- Full product CRUD + signed-delta inventory adjustment, Admin-only.
- Faceted search (`POST /api/products/search`) via a single MongoDB
  aggregation pipeline: category, price range, in-stock, free-text, and
  category-specific attribute filters, with facet counts returned
  alongside results in one round trip (ADR 0004).
- Compound wildcard attribute index + tenant-first compound indexes
  supporting every query shape the API issues (ADR 0002).
- `GET /health` (MongoDB ping), `GET /api/categories` (category/attribute
  metadata for building search UIs).
- Interactive API docs via Scalar (`/scalar`, Development only).
- Demo data seeding (two tenants, three users, six products) on startup
  outside `Production`.
- Multi-stage, non-root Dockerfile; `docker-compose.yml` (API + MongoDB).
- CI: format check, build, unit tests, integration tests (Testcontainers
  + real MongoDB), Docker image build (GitHub Actions).
- Full `docs/project-memory` set: brief, requirements, architecture,
  security, testing, ops, 4 ADRs, risk register, backlog, handoff.

### Known limitations at this release
- No JWT revocation, no login rate limiting -- see `risk.md` R3/R4.
- Facet counts computed net of the full filter, not independently per
  facet -- see ADR 0004.
- See `docs/project-memory/backlog.md` for the full deferred-work list.

### Verification status
See `docs/project-memory/handoff.md` for the exact CI/test/Docker/
Dependabot verification status as of this release, including what could
and could not be verified inside the development sandbox versus what was
confirmed by GitHub Actions CI.
