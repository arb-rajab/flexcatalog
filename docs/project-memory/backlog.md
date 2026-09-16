# Backlog

Deferred work, in rough priority order, with the reason each was deferred
rather than done now.

## Security hardening
1. **Refuse to start in `Production` if `Jwt:Secret` matches the known
   development placeholder value.** Cheap, closes R2 in `risk.md`.
2. **Rate limiting on `POST /api/auth/login`** (ASP.NET Core's built-in
   rate limiting middleware is enough). Closes R4.
3. **JWT revocation / short-lived-token-plus-refresh-token model.**
   Deferred because it meaningfully increases complexity (token storage,
   refresh flow, rotation) for a demo whose current 60-minute token
   lifetime is an accepted tradeoff (R3).

## Search
4. **Independent-branch faceted navigation** (ADR 0004) -- each facet
   computed net of every filter except its own, so switching a brand
   filter still shows the other available brands' counts. Deferred
   because it multiplies the aggregation pipeline's branch count and
   wasn't needed to demonstrate the core `$facet` pattern.
5. **Configurable/derived facet field list**, instead of the current
   hardcoded `["brand", "sizes", "colors", "author"]`. Would need a
   bounded-cardinality check (faceting on a field with thousands of
   distinct values is a performance trap) before being safe to open up.

## Multi-tenancy scale path
6. **Database-per-tenant migration path**, for if/when a tenant's data
   volume or isolation requirements outgrow the shared-collection model
   (ADR 0001 documents this as the intended next step, not a hypothetical
   one).
7. **Platform-admin, cross-tenant reporting role.** Explicitly out of
   scope today (ADR 0001) -- the repository layer has no method that
   queries across tenants, by design; adding one would be a deliberate,
   separately-reviewed change.

## Operability
8. **Structured logging + basic OpenTelemetry tracing.** Would be the
   first thing added in a real production-hardening pass (R9).
9. **Seed-data reset endpoint or CLI** for demo/QA environments, instead
   of relying on "seeding only happens once, when the tenants collection
   is empty."

## Product features (contrived-scope, lowest priority)
10. A fourth category (e.g. Groceries) purely to further demonstrate that
    adding a category is a code-only change (ADR 0002) -- not needed to
    prove the point (three categories already do), listed for
    completeness.
11. Bulk product import/export.
12. Soft-delete instead of hard-delete for products (audit trail).

## Testing
13. Load/performance test against a larger synthetic catalog, to put a
    number on how the wildcard attribute index (ADR 0002) behaves at
    scale rather than asserting it qualitatively.
14. Contract tests against the generated OpenAPI document.
