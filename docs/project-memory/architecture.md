# Architecture

## Stack

- **Runtime**: ASP.NET Core on .NET 10 (latest stable at time of writing),
  Minimal APIs (no MVC controllers -- the endpoint surface is small enough
  that route-group extension methods are clearer than controller
  boilerplate).
- **Datastore**: MongoDB 7+ (official `MongoDB.Driver` C# driver).
- **Auth**: JWT bearer (`Microsoft.AspNetCore.Authentication.JwtBearer`),
  self-issued -- see ADR 0003.
- **API docs**: built-in `Microsoft.AspNetCore.OpenApi` + Scalar UI
  (`/scalar`, Development only) -- not Swashbuckle; see
  "Why not Swashbuckle" below.
- **Tests**: xUnit. Unit tests run with no external dependencies.
  Integration tests use Testcontainers to run against a real, ephemeral
  MongoDB container.

## Layering

```
Endpoints/          Minimal API route groups. Thin: parse request -> call
                     a service -> map result to an HTTP response. No
                     business logic here.
Services/            Business logic + orchestration (ProductService,
                     ProductSearchService, AuthService). Throw typed
                     exceptions (NotFoundException, ConflictException,
                     ValidationException) instead of returning HTTP
                     status codes directly -- keeps services HTTP-agnostic
                     and testable without a running host.
Repositories/         The only classes that hold an IMongoCollection<T>.
                     Enforce tenant scoping structurally (ADR 0001).
Domain/               POCOs mapped to MongoDB documents (Product, Tenant,
                     User) and the polymorphic ProductAttributes
                     hierarchy (ADR 0002).
Auth/                 JWT issuance/validation glue, password hashing,
                     ITenantContext (resolves tenant/role/user id from
                     the current request's validated claims).
Infrastructure/       MongoContext (collection accessors), index creation,
                     demo data seeding, the global exception-to-
                     ProblemDetails mapper, Mongo naming conventions.
Dtos/                 Request/response records for the HTTP boundary.
```

Dependencies point one direction: Endpoints -> Services -> Repositories ->
Infrastructure. Nothing in `Domain` or `Repositories` depends on
`Endpoints` or ASP.NET Core request types.

## Request flow: a search request

```
POST /api/products/search
  -> ProductEndpoints (deserializes ProductSearchRequest, requires auth)
  -> ProductSearchService.SearchAsync
       - builds a $match + $facet aggregation pipeline (pure function,
         unit-tested independently of Mongo -- ADR 0004)
  -> ProductRepository.AggregateTenantScopedAsync
       - prepends {$match: {tenantId: <from JWT>}} unconditionally
       - runs the pipeline against MongoDB, returns one BsonDocument
  -> ProductSearchService reshapes the BsonDocument into
     ProductSearchResponse (items, facets, paging)
  -> 200 OK
```

## Multi-tenancy (ADR 0001) and auth (ADR 0003) in one picture

```
Client --Authorization: Bearer <jwt>--> ASP.NET Core JWT middleware
                                              |
                                    validates signature/issuer/audience/expiry
                                              |
                                    ClaimsPrincipal (sub, tenant_id, role)
                                              |
                                    ITenantContext (scoped per request,
                                    reads the claims above; throws if a
                                    claim is missing -- which only happens
                                    if an endpoint forgot [Authorize])
                                              |
                              every ProductRepository call takes its
                              tenant filter from here, not from anything
                              the client sent in the request body/headers
```

## Why not Swashbuckle

`Microsoft.OpenApi` v2 (the transitive dependency of the Swashbuckle
version compatible with .NET 10 at the time this was built) restructured
its namespaces in a way that no longer matches most existing Swashbuckle
usage examples. Rather than pin to an older, less-maintained combination,
this project uses the OpenAPI generation now built into ASP.NET Core
itself (`AddOpenApi()`/`MapOpenApi()`, available since .NET 9) plus
`Scalar.AspNetCore` for the interactive UI. This is arguably the more
current idiom for a .NET 10 project started today, not just a workaround.

## Configuration

All configuration is environment-variable-overridable
`appsettings.json` (`Mongo:ConnectionString`, `Mongo:DatabaseName`,
`Jwt:Secret`, `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpiryMinutes`). No
connection string or secret is hardcoded outside
`appsettings.Development.json`, which carries only a local-dev-only
placeholder (see `security.md`).

## What's deliberately not here

- No caching layer (Redis, in-memory). Catalog reads go straight to
  MongoDB; at this scale, and with the indexes in ADR 0002, that's fast
  enough, and adding a cache invalidation story would be complexity this
  demo doesn't need to justify.
- No message queue / event bus. Inventory adjustment is a synchronous
  read-modify-write; there's no fan-out to other systems to decouple.
- No API gateway / BFF layer. One API, one set of clients (documented as
  a single deployable unit in `ops.md`).
