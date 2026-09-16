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

## Secrets

- `Jwt:Secret` **must** be overridden via environment variable
  (`Jwt__Secret`) or a secret manager in any non-local environment.
  `Program.cs` throws at startup if it's empty or missing, so a
  deployment can't silently run unsigned -- but nothing currently stops a
  deployment from reusing the development placeholder value if an
  operator ignores this document. That is a manual operational discipline
  requirement, not something the code enforces, and is called out
  explicitly here for that reason.
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

- No rate limiting on `/api/auth/login` (brute-force mitigation).
- No JWT revocation list -- a compromised token is valid until it
  naturally expires (default 60 minutes).
- No audit log of who changed what product/inventory record.
- No CSRF concerns apply (this is a bearer-token API with no cookie-based
  session, consumed by non-browser or SPA clients that attach the
  Authorization header explicitly).
