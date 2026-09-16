# ADR 0001: Multi-Tenant Isolation Strategy

- Status: Accepted
- Date: 2026-09-16

## Context

FlexCatalog serves multiple tenants (separate businesses, each with their
own catalog, users, and inventory) from one deployment. Every read and
write must be scoped to the calling tenant; a bug that leaks one tenant's
products into another tenant's response is the single worst failure mode
this system can have.

Three standard strategies exist for multi-tenant data isolation:

1. **Database-per-tenant.** Each tenant gets its own MongoDB database (or
   cluster). Strongest isolation -- a query literally cannot reach another
   tenant's data because it never connects to that database. Cost: every
   new tenant is a provisioning operation, connection pooling multiplies
   per tenant, cross-tenant admin/reporting queries require fan-out, and
   at demo/portfolio scale (a handful of tenants) this is pure overhead.
2. **Collection-per-tenant.** Same tradeoffs as database-per-tenant but one
   level down -- still an operational multiplier (index management,
   collection creation) for no isolation benefit over option 3 as long as
   the application layer is trusted, and MongoDB has practical limits on
   collection count per database that make this awkward at real scale.
3. **Shared collection + `tenantId` discriminator field**, with every
   query required to filter on it. Weakest isolation *in the database
   itself* -- a raw, hand-written query that forgets the filter will
   return cross-tenant data. Isolation instead depends on the application
   layer enforcing the filter consistently.

## Decision

**Shared collection + `tenantId` field (option 3), with isolation enforced
structurally in the repository layer, not by convention.**

Every domain document (`Product`, `User`) carries a `tenantId` field. The
`tenantId` itself comes only from the validated JWT (`ITenantContext`,
resolved from the `tenant_id` claim) -- never from a client-supplied
header, query string, or request body field. A header can be forged by
anyone who can send an HTTP request; a JWT claim cannot be forged without
the signing key.

The structural enforcement mechanism: **`ProductRepository` is the only
code in the application that is allowed to touch the `products`
collection**, and every method on it that reads, writes, or aggregates
takes the tenant filter from `ITenantContext` and applies it
unconditionally:

- `GetByIdAsync` / `GetBySkuAsync` / `DeleteAsync` / `ReplaceAsync` all AND
  the caller's filter with `tenantId == ITenantContext.TenantId`.
- `InsertAsync` overwrites whatever `TenantId` the caller set on the
  in-memory object with the authenticated tenant's own ID before writing
  -- a client cannot even choose which tenant to write into.
- `AggregateTenantScopedAsync` (used by the faceted search pipeline)
  forcibly prepends a `{$match: {tenantId: ...}}` stage to *any* pipeline
  handed to it. There is no aggregation entry point that skips this.

Because no other class holds a reference to
`IMongoCollection<Product>`, there is no code path in the application that
can query products without going through this tenant-scoped gate. This
doesn't make cross-tenant leakage *impossible* (a `IMongoDatabase` is
still technically reachable via DI if someone deliberately bypassed the
repository), but it makes it require a deliberate, reviewable decision to
add a new unscoped access path, rather than a single forgotten `.Find()`
call in a services or endpoint file.

Login is a deliberate exception: `IUserRepository` is *not* tenant-scoped,
because tenant membership isn't known until after the username is
resolved (see ADR 0003). Usernames are globally unique for this reason.

## Consequences

- New collections that carry tenant data (e.g. if orders were added later)
  must follow the same pattern: a dedicated repository, `tenantId` taken
  only from `ITenantContext`, no other class touching the raw collection.
  This is a convention that has to be maintained by code review, not
  something the compiler enforces -- documented here explicitly so a
  future contributor doesn't quietly add a second, unscoped access path.
- Every tenant-scoped index (see ADR 0002) puts `tenantId` first in the
  compound key, so scoping isn't just correct but cheap -- Mongo can use
  the index prefix to skip straight to a tenant's data.
- Cross-tenant admin/reporting (e.g. "show me total inventory value across
  all tenants") is out of scope for this system and not supported by the
  current repository API. If a future platform-admin role needs it, that
  is a new, explicitly-named repository method (e.g.
  `AggregateAcrossAllTenantsAsync`), not a relaxation of the existing one
  -- tracked in `backlog.md`.
- `docs/project-memory/testing.md` requires an integration test proving
  cross-tenant isolation over real HTTP + real MongoDB, not just a unit
  test of the repository in isolation -- see
  `tests/FlexCatalog.IntegrationTests/Endpoints/TenantIsolationTests.cs`.

## Alternatives considered and rejected

- **Trusting a client-supplied `X-Tenant-Id` header.** Rejected outright:
  this makes tenant isolation a matter of the client's honesty, which is
  not isolation at all.
- **Row-level security equivalent in the database.** MongoDB has no
  built-in row-level security comparable to PostgreSQL RLS; a schema
  validation rule could reject documents with a missing `tenantId` but
  cannot restrict *queries*, so it doesn't solve the read side.
- **Database-per-tenant**, revisited: this is the right call once tenant
  count, per-tenant data volume, or regulatory data-residency requirements
  grow large enough that noisy-neighbor or blast-radius concerns outweigh
  the operational cost. Documented as a scaling path in `risk.md`, not
  implemented here because it would be premature engineering for a
  portfolio-scale demo.
