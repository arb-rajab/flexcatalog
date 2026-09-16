# Requirements

## Functional

### Auth
- FR-1: A user authenticates with `{username, password}` and receives a
  JWT carrying their tenant and role. (`POST /api/auth/login`)
- FR-2: All catalog endpoints except login and `/health` require a valid,
  unexpired JWT.
- FR-3: Write operations (create/update/delete product, adjust inventory)
  require the `Admin` role; read/search operations require any
  authenticated role.

### Catalog
- FR-4: A product belongs to exactly one category (`Electronics`,
  `Apparel`, `Books`) and carries an attribute bag whose shape matches
  that category. A product cannot be saved with a mismatched attribute
  shape (e.g. Electronics category with Apparel attributes).
- FR-5: SKUs are unique per tenant (not globally). Creating a product
  with a SKU that already exists for the caller's tenant fails with 409.
- FR-6: Admins can create, read, update, and delete products within their
  own tenant only.
- FR-7: Admins can adjust a product's stock quantity by a signed delta;
  the operation fails (400) if it would drive quantity negative, and
  `inStock` is derived automatically from the resulting quantity.

### Search
- FR-8: Any authenticated user can search their tenant's products,
  filtering by category, price range, in-stock status, free text, and
  category-specific attributes (e.g. brand, size, color, author).
- FR-9: Search results are paginated and sortable (price ascending/
  descending, newest, relevance when a text query is present).
- FR-10: Search responses include facet counts (per-category, per
  selected attribute field, and the price range of the result set) in
  the same response as the results.

### Multi-tenancy
- FR-11: A tenant's data (products, and by extension users) is never
  visible or mutable from another tenant's authenticated session, under
  any endpoint.
- FR-12: Tenant identity for a request is derived solely from the
  validated JWT, never from a client-supplied header or parameter.

## Non-functional

- NFR-1 (Isolation, hard requirement): see FR-11 -- verified by
  integration tests over real HTTP + real MongoDB, not just unit tests of
  the repository layer.
- NFR-2 (Schema flexibility): adding a new product category must not
  require a database migration -- only a new C# attribute type and
  discriminator value.
- NFR-3 (Query performance): every query pattern the API actually issues
  (tenant-scoped lookups, category+price browsing, in-stock filtering,
  attribute filtering, text search) is backed by an index; see ADR 0002.
- NFR-4 (Security): passwords are hashed (BCrypt), never logged or
  returned in any response; JWT secret is never committed with a
  production-usable value (see `security.md`).
- NFR-5 (Portability): the API runs identically via `dotnet run` or the
  published Docker image, against any MongoDB 7+ instance, with
  configuration entirely via environment variables / `appsettings.json`
  (12-factor style, no hardcoded connection info).
- NFR-6 (Testability): business logic (validation, query-building,
  token issuance) is unit-testable without a live database; cross-cutting
  behavior (auth, tenant isolation, end-to-end search) is covered by
  integration tests against a real MongoDB via Testcontainers.
- NFR-7 (Non-root container): the runtime container process runs as a
  non-root user.

## Explicitly not required

- Horizontal scale-out validation (single-instance deployment is the
  target for this demo; see `ops.md` for the scaling story).
- Multi-region / data-residency guarantees.
- Sub-second search on catalogs larger than what fits comfortably in a
  single unsharded MongoDB replica set (see `risk.md`).
