# Risk Register

| # | Risk | Likelihood | Impact | Mitigation / status |
|---|---|---|---|---|
| R1 | Cross-tenant data leakage (one tenant sees another's data) | Low (structurally mitigated) | Critical | Repository-enforced tenant scoping (ADR 0001) + dedicated integration test suite (`TenantIsolationTests`). Residual risk: a future contributor adds a new tenant-scoped collection without following the same pattern -- mitigated by documenting the convention explicitly in ADR 0001, not by a compiler check. |
| R2 | JWT secret reused from the committed development placeholder in a real deployment | Medium (depends on operator discipline) | Critical | App fails to start with an empty secret, but does not detect or reject the *specific* known placeholder value. Documented prominently in `security.md` and `ops.md`. Follow-up: a startup check that refuses to boot in `Production` if `Jwt:Secret` equals the known dev placeholder string (cheap to add, tracked in `backlog.md`). |
| R3 | Compromised JWT is valid until natural expiry (no revocation) | Low-Medium | Medium | Token lifetime capped at 60 minutes by default; no revocation list exists. Acceptable for this system's scope; real production use would need a blocklist or move to short-lived tokens + refresh tokens (`backlog.md`). |
| R4 | Brute-force login attempts (no rate limiting) | Medium | Medium | Not mitigated. BCrypt's cost factor (12) slows single-attempt hashing but doesn't stop distributed brute force. Tracked in `backlog.md`. |
| R5 | Faceted search attribute-filter keys used as a NoSQL injection vector | Low (mitigated) | High | Keys validated against a strict allowlist regex before being used to build a Mongo field path; values are always passed as typed BSON, never string-concatenated. Covered by a dedicated test (`BuildMatchDocument_RejectsUnsafeAttributeKeys`). |
| R6 | Compound wildcard attribute index (ADR 0002) has higher write-time index-maintenance cost than a handful of targeted indexes | Medium | Low-Medium at current scale | Accepted tradeoff for schema flexibility across categories; revisit if/when write throughput on `products` becomes a measured bottleneck -- at that point, targeted per-field indexes for the highest-traffic attributes could supplement or replace the wildcard index. |
| R7 | Faceted search facet counts are computed net of the *full* filter, not independently per facet (ADR 0004) | N/A (documented behavior, not a defect) | Low | Documented explicitly in ADR 0004 and `architecture.md` rather than silently shipped as the more sophisticated independent-branch behavior. Follow-up in `backlog.md`. |
| R8 | Single shared MongoDB database for all tenants means one tenant's very large or very hot dataset can affect others ("noisy neighbor") | Low at demo scale | Medium if it ever ran at real scale | ADR 0001 documents database-per-tenant as the scaling path if this ever became a real concern; not implemented because it would be premature for this system's actual scale and purpose. |
| R9 | No centralized/structured logging or tracing | High (true today) | Low for a demo, Medium for real ops | Standard ASP.NET Core console logging only. Acceptable for a portfolio demo; tracked in `backlog.md` as what a production hardening pass would add first. |
| R10 | Integration test suite (Testcontainers) could not be executed inside the development sandbox due to an egress policy blocking Docker Hub | N/A (environment limitation, not a product risk) | N/A | Verified instead by GitHub Actions CI, which has unrestricted internet access; see `testing.md` and `ops.md` for exact detail and `handoff.md` for the confirmed CI result. |

## How this list is used

Reviewed whenever a new feature touches auth, tenant scoping, or search
query-building -- those three areas carry the risks with the highest
impact (R1, R2, R5). Anything added to `backlog.md` that closes one of
these rows should update the row's status here rather than leaving it
stale.
