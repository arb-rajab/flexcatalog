# Security

## Authentication & authorization

- JWT bearer, HS256, self-issued -- full reasoning in ADR 0003.
- Passwords hashed with BCrypt (work factor 12) via `IPasswordHasher`;
  never stored, logged, or echoed back in plaintext anywhere, including
  error messages.
- Login returns an identical error for "unknown username" and "wrong
  password" -- prevents username enumeration via the login endpoint.
- Two roles (`Admin`, `Viewer`), enforced via `RequireClaim("role", ...)`
  policy on write endpoints. No per-resource ACL beyond tenant + role.
- `POST /api/auth/login` is rate-limited: 5 attempts per 60-second fixed
  window, partitioned per client IP, HTTP 429 with no queueing past the
  limit (`FlexCatalog.Api.Infrastructure.LoginRateLimiting`, ASP.NET
  Core's built-in rate limiter). Closes risk.md R4. Change the threshold
  in `LoginRateLimiting` and here together if it's ever revisited.
  Known limitation: partitioning is per-IP, so a distributed attack
  spread across many source IPs isn't slowed by this alone.

## Secrets

- `Jwt:Secret` **must** be overridden via environment variable
  (`Jwt__Secret`) or a secret manager in any non-local environment.
  `Program.cs` throws at startup if it's empty or missing, so a
  deployment can't silently run unsigned. It also now throws at startup
  (`JwtSecretGuard.EnsureNotPlaceholder`) if the app is running in
  `Production` and the configured secret is exactly the known
  `appsettings.Development.json` placeholder value -- closes risk.md R2.
  This only catches *that specific known string*; nothing enforces that
  an operator-chosen replacement is actually strong. That residual case
  is a manual operational discipline requirement, not something the code
  can meaningfully check, and is called out explicitly here for that
  reason.
- The value committed in `appsettings.Development.json` is a placeholder
  string, clearly commented as local-dev-only, and is not secret-scanned
  as a real credential because it isn't one -- it's meaningless outside a
  developer's own machine (tokens signed with it are only ever validated
  by that same machine's own instance of the API).
- `docker-compose.yml` requires `FLEXCATALOG_JWT_SECRET` to be set in the
  environment before `docker compose up` will start the API
  (`${FLEXCATALOG_JWT_SECRET:?...}` -- Compose fails fast with a clear
  message rather than silently falling back to nothing).

## Tenant isolation as a security control

Covered in depth in ADR 0001. From a security-review angle specifically:
the risk this addresses is horizontal privilege escalation (one tenant
reading/writing another tenant's data), which for a multi-tenant SaaS is
typically the highest-severity class of bug. The mitigation is structural
(repository-enforced), not just tested-for, and there is a dedicated
integration test suite
(`tests/FlexCatalog.IntegrationTests/Endpoints/TenantIsolationTests.cs`)
that exercises it over real HTTP against a real database, not just a
repository-level unit test.

## Input handling

- Faceted-search attribute filter keys are validated against
  `^[a-zA-Z][a-zA-Z0-9]*$` before being used to build a MongoDB field
  path, specifically to prevent a caller from injecting a Mongo operator
  (e.g. a filter key of `$where`) into the query. See ADR 0004 and
  `ProductSearchServiceQueryBuildingTests.BuildMatchDocument_
  RejectsUnsafeAttributeKeys`.
- All other user-supplied values (search terms, attribute filter values,
  product fields) are passed to MongoDB as typed BSON values via the
  driver's builders, not string-concatenated into any query -- there is
  no code path in this project that constructs a MongoDB query via string
  interpolation.
- Request/response DTOs are `record` types with explicit shapes; there is
  no reflection-based "bind anything" model binding that could pick up
  unexpected fields.

## Transport

- `UseHttpsRedirection()` is applied only in Development. In non-Development
  environments (including the shipped Docker image), TLS is assumed to
  terminate upstream (reverse proxy / load balancer / platform ingress) --
  documented in `architecture.md` and `ops.md`. This is a standard
  containerized-service pattern, not a gap, but it does mean whoever
  deploys this image is responsible for terminating TLS somewhere before
  traffic reaches it.

## Dependencies

- Dependency vulnerability status for this repository: see `handoff.md`
  for the actual verified/unverified status at time of delivery --
  documented there rather than duplicated here so it isn't accidentally
  left stale in two places.

## What's explicitly not implemented (see risk.md / backlog.md for follow-up)

- No JWT revocation list -- a compromised token is valid until it
  naturally expires (default 60 minutes). Explicitly deferred rather than
  half-shipped; see `backlog.md` item 3 for the reasoning.
- No audit log of who changed what product/inventory record.
- No CSRF concerns apply (this is a bearer-token API with no cookie-based
  session, consumed by non-browser or SPA clients that attach the
  Authorization header explicitly).
