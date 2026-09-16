# ADR 0003: JWT Authentication and Login Model

- Status: Accepted
- Date: 2026-09-16

## Context

The API needs to authenticate callers and establish, for every request,
both *who* they are and *which tenant* they belong to -- the tenant claim
is what `ITenantContext` (ADR 0001) relies on to scope every query. It
also needs a coarse authorization model: an Admin can write to the
catalog and adjust inventory; a Viewer can only read and search.

This repo's portfolio convention is JWT for API auth, matching the other
services in the same portfolio, and there's no reason to deviate here:
FlexCatalog is a stateless API with no server-rendered session, JWT
avoids a server-side session store, and it carries exactly the two claims
(`tenant_id`, `role`) that every downstream authorization decision in this
API needs, without a database lookup per request.

## Decision

**Self-issued HS256 JWTs, via `POST /api/auth/login`, with `tenant_id` and
`role` as custom claims.**

- `IJwtTokenService` signs tokens with a symmetric secret
  (`Jwt:Secret`, `HmacSha256`). The token embeds `sub` (user id),
  `tenant_id`, `role`, and a `jti`. `Program.cs` wires
  `AddJwtBearer` to validate issuer, audience, lifetime, and signature
  against the same secret.
- **Usernames are globally unique across all tenants**, not
  unique-per-tenant. `POST /api/auth/login` takes only `username` +
  `password` -- no tenant selector. This is a deliberate choice: it
  mirrors how most real multi-tenant SaaS products work (you log in with
  your email; which workspace/tenant you land in is determined by your
  account, not something you pick from a dropdown before you've even
  authenticated). The alternative -- `{tenant, username, password}` on
  login -- pushes tenant *identification* onto the end user before
  they've proven who they are, which is both worse UX and doesn't remove
  any actual security requirement (the tenant claim in the resulting
  token still has to come from the server's own user record either way).
- Passwords are hashed with BCrypt (work factor 12) via
  `IPasswordHasher`, never stored or logged in plaintext.
- Login failure (unknown username vs. wrong password) returns the same
  400 response with the same message in both cases
  (`AuthService.LoginAsync`) -- distinguishing them would let a caller
  enumerate valid usernames.
- Authorization is a single coarse policy: `RequireClaim("role",
  "Admin")` on write endpoints (`POST`/`PUT`/`DELETE` on products,
  inventory adjustment). Read and search endpoints require only a valid
  token (any role). There is no per-resource ACL -- every Admin in a
  tenant can manage every product in that tenant; finer-grained
  permissions are out of scope (see `backlog.md`).

## Consequences

- The signing secret is a shared symmetric key
  (`Jwt:Secret`), which means anything that can mint a valid token must
  be trusted with that secret -- there is exactly one issuer (this API
  itself). This is appropriate for a single-service demo; it would not be
  appropriate if a second service needed to *verify* FlexCatalog's tokens
  independently (see Alternatives).
- The local-development secret in `appsettings.Development.json` is
  intentionally a placeholder and is documented as such (see
  `security.md`) -- Program.cs throws at startup if `Jwt:Secret` is
  empty, so a deployment can't silently run with no signing key, but
  nothing stops a deployment from reusing the dev placeholder if an
  operator ignores the documentation. Real deployments must inject
  `Jwt__Secret` via environment variable / secret manager, never commit a
  production secret to `appsettings.json`.
- Tokens are stateless and not revocable before expiry (`ExpiryMinutes`,
  default 60). There is no token blocklist. This is an accepted tradeoff
  for this system's scope -- see `risk.md` for the operational
  consequence (a compromised token is valid until it expires, at most 60
  minutes) and `backlog.md` for a revocation-list follow-up if this ever
  needed to be production-hardened.
- Because tenant membership is fixed at token-issue time, a user moved to
  a different tenant (not currently a supported operation) would need
  their existing tokens to expire or be invalidated before the change
  takes effect -- another consequence of no revocation list.

## Alternatives considered and rejected

- **API keys instead of JWT**: simpler to implement, but doesn't carry
  claims (tenant, role) without a database lookup per request, which
  reintroduces exactly the per-request lookup JWT avoids. Rejected to
  stay consistent with the portfolio's JWT convention and because the
  claims-in-token model fits this API's stateless design well.
- **RS256 (asymmetric) instead of HS256**: the right choice once a
  *second* service needs to verify FlexCatalog's tokens without holding
  the ability to mint them (i.e., needs the public key but not the
  private key). Not implemented here because FlexCatalog is the sole
  issuer and sole verifier of its own tokens in this system -- HS256 is
  simpler operationally (one secret, not a keypair + rotation story) and
  the asymmetry RS256 buys isn't needed yet. Documented as the upgrade
  path if a second verifying service is ever added.
- **External IdP (Auth0/Entra ID/Cognito)**: the right choice for a real
  production multi-tenant SaaS, and would also solve token revocation.
  Rejected for this repo specifically because it's a portfolio skill-demo
  aimed at showing .NET + MongoDB engineering, not IdP integration -- and
  it would require the reader to provision an external account just to
  run the demo. Noted as the realistic production path in `security.md`.
- **Tenant selector on login (`{tenant, username, password}`)**:
  rejected as described above -- doesn't add security, adds UX friction
  and a duplicate source of truth for "which tenant is this user in."
