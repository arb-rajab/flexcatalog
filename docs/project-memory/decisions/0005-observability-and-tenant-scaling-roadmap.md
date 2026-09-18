# ADR 0005: Observability and Database-per-Tenant Scaling Roadmap

- Status: Proposed (design only -- not implemented)
- Date: 2026-09-17

## Context

Session priority order put security hardening (R2/R4, JWT revocation
decision) and independent-branch faceted navigation ahead of two
lower-urgency, higher-effort backlog items: structured logging/tracing
(R9, backlog item 8) and the database-per-tenant scaling path (R8,
backlog item 6, already flagged as a real next step in ADR 0001 rather
than a hypothetical). Both were deliberately not implemented this
session -- per the session brief, this ADR is the design-level output in
their place, rather than a rushed partial implementation of either.

This document is intentionally a plan, not a change: it exists so the
next session that picks either of these up doesn't have to start from
"what would this even look like," and so the scope and triggers are
written down while the reasoning is fresh, rather than re-derived later
under time pressure.

## Part 1: Structured logging + tracing

### Current state

Standard ASP.NET Core console logging (`ILogger<T>`), unstructured,
no correlation ID propagation beyond whatever the hosting platform
attaches, no tracing spans around the MongoDB calls that dominate this
service's latency.

### Proposed design

1. **Structured logging**: adopt the built-in
   `Microsoft.Extensions.Logging` JSON console formatter
   (`options.FormatterName = ConsoleFormatterNames.Json` via
   `AddJsonConsole`) rather than pulling in Serilog/NLog -- this system's
   log volume and query needs don't justify a third-party sink, and JSON
   console output is directly consumable by any container log collector
   (CloudWatch, Loki, whatever the deployment platform uses) without a
   FlexCatalog-specific exporter config.
2. **Correlation**: enable `W3C` trace-context propagation
   (`Activity.DefaultIdFormat = ActivityIdFormat.W3C`, on by default in
   .NET) and add the trace ID to every log scope via
   `IHttpContextAccessor` in a small logging middleware, so every log line
   from one request carries the same ID without manually threading one
   through every method signature.
3. **Tracing**: add `OpenTelemetry.Extensions.Hosting` +
   `OpenTelemetry.Instrumentation.AspNetCore` +
   `OpenTelemetry.Instrumentation.MongoDB` (the MongoDB driver supports
   `DiagnosticSource` activities natively as of the driver version this
   project already depends on -- no code change needed in
   `ProductRepository`/`MongoContext`, just enabling the instrumentation
   in `Program.cs`). Export via OTLP to whatever collector the deployment
   environment provides; console exporter for local dev.
4. **What to instrument first, in priority order**: (a) the faceted
   search pipeline (`ProductSearchService.SearchAsync`) -- it's the
   costliest single operation and the one most likely to need a "why did
   this request take 400ms" answer; (b) JWT validation failures (a spike
   is either an attack or a client bug, and today it's invisible); (c)
   the rate limiter's rejection count (`LoginRateLimiting`) as a metric,
   not just a 429 response -- without it, nobody would notice if the
   threshold were ever mistuned.

### Explicitly out of scope for a first pass

A metrics backend/dashboard (Prometheus, Grafana) -- that's a deployment
concern, not an application-code concern; the application's job is to
emit `System.Diagnostics.Metrics` counters/histograms via
`OpenTelemetry.Instrumentation.*`, and whatever scrapes/renders them is
an ops decision outside this repo.

### Estimated scope

Small-to-medium: mostly package references + `Program.cs` wiring (similar
shape to the JWT/rate-limiter wiring already there), no domain logic
changes. The honest reason this wasn't done in the same session as items
1-4: it's the kind of change best verified by actually looking at
emitted traces/logs, which needs a running collector or at least a local
run against real MongoDB -- exactly the thing this sandbox can't do (see
`testing.md`). Low risk of the "half-shipped" failure mode item 3 was
explicitly avoiding, but still better attempted with real verification
available rather than build-only.

## Part 2: Database-per-tenant scaling path

### Current state

Shared `products`/`users`/`tenants` collections in one database, isolated
by `tenantId` (ADR 0001). This is deliberately not a hypothetical
future path -- ADR 0001 already names it as *the* scaling answer, not
one option among several.

### Trigger conditions (any one of these is sufficient to start the migration, not just discuss it)

1. **Noisy-neighbor impact becomes measurable**: one tenant's query
   latency visibly degrades another tenant's, observable once Part 1's
   tracing exists (a specific dependency this has on Part 1 -- without
   per-tenant latency attribution, "noisy neighbor" is a guess, not a
   measurement).
2. **A tenant requires data residency or regulatory isolation** that a
   shared database structurally cannot satisfy (e.g. contractual
   requirement that tenant data never share physical storage with another
   customer).
3. **Write throughput on `products` approaches the compound wildcard
   index's maintenance cost becoming a bottleneck** (R6 in `risk.md`) --
   database-per-tenant isn't the fix for this one on its own, but it's
   the point at which per-tenant index tuning becomes viable, which is.

### Proposed migration approach

1. **Routing layer first, data migration second.** Introduce a
   `ITenantDatabaseResolver` (or fold into `ITenantContext`) that maps
   `tenantId` -> a MongoDB connection string / database name, defaulting
   every existing tenant to the current shared database. This makes
   "which database do I connect to" a resolved-per-request concern
   instead of a single `MongoContext` singleton, without moving any data
   yet -- ships and is verifiable (existing `TenantIsolationTests` should
   still pass unchanged, since all tenants still resolve to the same
   database) before any tenant's data actually moves.
2. **Migrate one tenant at a time**, not a flag day: (a) provision the
   new database, (b) copy that tenant's documents across (a `tenantId`
   filter is already exactly the query needed), (c) flip that tenant's
   resolver entry, (d) verify, (e) delete the old copy only after a
   verification window. `ProductRepository` doesn't need to change at
   all for this -- it already only ever knows about one resolved
   `IMongoCollection<Product>` per request; the resolver decides which
   one.
3. **`DataSeeder` and `MongoIndexInitializer`** (both currently
   singletons that assume one database) need to become tenant-aware or
   move to a per-tenant-database provisioning step -- this is the actual
   nontrivial part of the migration, not the query routing, because it's
   where the two DI-lifetime bugs documented in `handoff.md` originated;
   any implementation of this must re-run the DI-graph-only host-start
   check (`CLAUDE.md`) specifically because that's exactly the class of
   bug this system has already hit twice.
4. **Cross-tenant admin/reporting stays out of scope** regardless of this
   migration (ADR 0001) -- database-per-tenant makes that fan-out
   *more* expensive, not less, which is a consideration for if/when that
   feature is ever prioritized, not a blocker for this migration.

### Explicitly not proposed

A big-bang "migrate everyone at once" cutover, and building the
multi-database routing speculatively before any trigger condition is
actually met -- both are premature engineering for a system currently
serving two demo tenants.

## Consequences of writing this as a design doc rather than code

- Nothing in this repository's behavior changes as a result of this ADR.
- The next session (or the next backlog pass) has a concrete starting
  point for either item instead of the one-line backlog entries that
  existed before, without the "committed to an implementation you
  can't verify against real infrastructure in this sandbox" risk noted
  in Part 1's estimated scope.
- `backlog.md` items 6 and 8 are updated to point here rather than
  duplicating this reasoning inline.
