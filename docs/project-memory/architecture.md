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
  Integration tests use Testcontainers to run against real, ephemeral
  MongoDB, NATS, and Meilisearch containers.
- **Event streaming**: NATS (core pub/sub), `NATS.Client.Core` -- one
  additive, fire-and-forget side channel off the REST/MongoDB core; see
  "Event streaming (ADR 0006)" below.
- **Search engine**: Meilisearch, official `Meilisearch` .NET client --
  additive full-text/typo-tolerant search alongside MongoDB's own $facet
  search, fed by the same NATS event stream; see "Search indexing
  (ADR 0007)" below.

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

Three more projects sit alongside `FlexCatalog.Api`: `FlexCatalog.Contracts`
(added by ADR 0006 -- event envelope/payload records and the Meilisearch
document shape, shared by every publisher/consumer, no logic) and two
independent consumers of the same event stream, `FlexCatalog.InventoryProjector`
(ADR 0006) and `FlexCatalog.SearchIndexer` (ADR 0007) -- each with its own
`Program.cs` and its own downstream connection (MongoDB, Meilisearch
respectively), no reference to `FlexCatalog.Api` or to each other in any
direction.

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

## Event streaming (ADR 0006)

Additive to everything above, not a replacement for any of it: the
REST/MongoDB core in the request-flow diagram above is unchanged and does
not depend on any of this working.

```
ProductService.CreateAsync / .AdjustInventoryAsync
  -> repository write already succeeded (Mongo)
  -> IDomainEventPublisher.Publish(...)      [in-memory, non-blocking,
                                               cannot fail the request]
       -> DomainEventChannel (bounded, drop-oldest)
            -> DomainEventPublishingService (Singleton BackgroundService,
               its own NatsConnection, catches/logs every publish failure)
                 -> NATS subject "flexcatalog.events.<event-type>"
                      -> FlexCatalog.InventoryProjector (separate process)
                           -> InventoryProjectionConsumer
                                -> its own MongoDB collection
                                   ("productInventoryProjection")
```

`FlexCatalog.Contracts` is a small shared class library (envelope +
per-event payload records + event-type constants) referenced by
`FlexCatalog.Api` (the only publisher) and every independent consumer
(`FlexCatalog.InventoryProjector`, `FlexCatalog.SearchIndexer`) -- all
sides agree on wire schema without depending on each other's code. See
ADR 0006 for the full broker choice reasoning, the fire-and-forget design,
and its consequences (at-most-once delivery, no replay, a consumer's own
downstream store is a convenience view that nothing else depends on).

## Search indexing (ADR 0007)

A second, independent consumer of the same event stream, added
specifically to demonstrate a dedicated search engine (Meilisearch)
alongside MongoDB's own $facet-based search (ADR 0004) -- additive, not a
replacement for either the event stream or the existing search endpoint.

```
ProductService.CreateAsync / .UpdateAsync / .DeleteAsync / .AdjustInventoryAsync
  -> repository write already succeeded (Mongo)
  -> IDomainEventPublisher.Publish(...)   [same channel as ADR 0006]
       -> NATS subject "flexcatalog.events.<event-type>"
            -> FlexCatalog.SearchIndexer (separate process, sibling of
               InventoryProjector -- same retry/backoff discipline)
                 -> SearchIndexingConsumer
                      -> Meilisearch index "products"
                           (add/update/delete a ProductSearchDocument)

GET-side (independent of the write path above):
POST /api/products/search/meilisearch
  -> ProductEndpoints -> MeilisearchProductSearchService.SearchAsync
       -> BuildFilter unconditionally ANDs tenantId (from ITenantContext,
          mirroring ADR 0001's Mongo tenant-scoping discipline)
       -> queries the same Meilisearch index SearchIndexingConsumer writes
```

`product.updated` and `product.deleted` are new events (ADR 0007) --
`ProductService.UpdateAsync`/`DeleteAsync` existed before this but
published nothing; without them the search index could never learn about
edits or removals and would drift from MongoDB indefinitely, not just lag
briefly. Added via the identical fire-and-forget hook pattern ADR 0006
already established (publish immediately after the Mongo write succeeds),
not a new mechanism.

## What's deliberately not here

- No caching layer (Redis, in-memory). Catalog reads go straight to
  MongoDB; at this scale, and with the indexes in ADR 0002, that's fast
  enough, and adding a cache invalidation story would be complexity this
  demo doesn't need to justify.
- No task queue (RabbitMQ/Redis-queue style point-to-point work
  distribution) -- inventory adjustment is a synchronous read-modify-write
  with no unit of work to hand off to exactly one worker. The
  event-streaming addition above (ADR 0006) is a deliberately different
  pattern -- broadcast facts to zero-or-more independent subscribers, not
  a job queue -- and doesn't change this.
- No API gateway / BFF layer. One API, one set of clients (documented as
  a single deployable unit in `ops.md`).
