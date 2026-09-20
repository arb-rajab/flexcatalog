# ADR 0006: Inventory Domain Events via NATS Core Pub/Sub

- Status: Accepted
- Date: 2026-09-19

## Context

Every write path in this repo today is synchronous REST-over-MongoDB: a
client calls an endpoint, the endpoint calls a service, the service reads
and writes MongoDB, and the HTTP response carries the full result. Nothing
in the system publishes a fact about what happened for another process to
react to independently. That's a deliberate scope cut recorded in
`architecture.md` ("no message queue / event bus... there's no fan-out to
other systems to decouple"), but it also means this portfolio has no
example of the event-streaming/pub-sub style of integration -- publishing
a durable-enough domain event that an independent consumer picks up to
build its own view of the world -- as distinct from the task-queue pattern
already shown elsewhere (a producer enqueues one job for one worker to
eventually process and dequeue, as in bookslot's RabbitMQ usage or
privacy-forge's Redis queues).

The two patterns solve different problems. A task queue distributes units
of work: each message is claimed and consumed by exactly one worker, and
losing the message before it's processed means the job never runs. Event
streaming publishes a fact about something that already happened, broadcast
to zero, one, or many independent subscribers, each free to build its own
downstream state from the same stream without the publisher knowing or
caring who's listening. This ADR is about adding a real example of the
second pattern -- `ProductCreated` / `InventoryAdjusted` events broadcast
from the existing write paths, consumed by an independent projection
service -- without touching the first pattern (there is no task-queue need
here; nothing in this repo needs to hand off a unit of work to exactly one
worker).

## Decision

**NATS (core pub/sub, not JetStream) as the broker**, added as a new
`nats` service in `docker-compose.yml` alongside `mongo`.

### Why NATS over the alternatives

- **Apache Kafka**: the canonical answer for "event streaming," but
  standing up a Kafka broker (plus, in practice, Zookeeper or KRaft
  controller quorum, and meaningfully more memory/startup time) is
  disproportionate to a single-topic, two-event demo. It would also make
  the Docker Compose stack and the CI integration-test startup
  meaningfully heavier for no corresponding gain in what the sample
  actually demonstrates. Kafka's durability/replay/partitioning model is
  the right answer at a scale this repo isn't operating at.
- **RabbitMQ**: already used elsewhere in this portfolio (bookslot) to
  demonstrate the *task-queue* pattern specifically. Reusing it here would
  blur the distinction this task exists to draw, rather than adding a
  second, genuinely different pattern to the portfolio. RabbitMQ can do
  pub/sub via fanout exchanges, but it isn't what it's reached for.
- **Redis Streams / pub-sub**: already used elsewhere (privacy-forge,
  Redis queues) for the task-queue pattern; same objection as RabbitMQ.
  Redis pub-sub is also fire-and-forget with no consumer-group replay
  story, so it wouldn't add anything NATS core pub/sub doesn't already
  give here.
- **NATS (core, no JetStream)**: a single static binary, ~10MB image,
  starts in under a second, and its core pub/sub model -- publish to a
  subject, any current subscriber gets it, no persistence, no consumer
  acknowledgment -- is a direct, honest match for requirement #4 of this
  task: publishing must be best-effort and must never be able to fail or
  slow the primary write path. NATS core doesn't persist messages or
  retry delivery, which sounds like a limitation until you notice it's
  exactly the semantic this task asks for: if no consumer is currently
  subscribed, or the broker is briefly unreachable, the event is silently
  dropped and the REST/MongoDB write already succeeded and returned to the
  caller. Reaching for JetStream (NATS's persistence/replay layer, its
  answer to "Kafka-like" durability) would mean opting into
  acknowledgment, retry, and redelivery semantics that this task
  explicitly does not want for the primary write path -- that's a
  different, stronger guarantee than "additive, best-effort side channel."
  If a future consumer genuinely needs at-least-once delivery or replay
  from an arbitrary point in time, JetStream is the documented upgrade
  path (same client library, same subjects, opt in per-stream) --
  intentionally not built now; see "Consequences" below.

### Wire format

One subject namespace, `flexcatalog.events.<event-type>` (currently
`flexcatalog.events.product.created` and
`flexcatalog.events.inventory.adjusted`), carrying a small JSON envelope:

```json
{
  "eventId": "…",
  "eventType": "product.created",
  "tenantId": "…",
  "occurredAtUtc": "2026-09-19T12:00:00Z",
  "payloadJson": "{ …event-specific fields, itself JSON-encoded… }"
}
```

The envelope/payload split (rather than one flat JSON object per event
type) and the shared `FlexCatalog.Contracts` project that defines it are
what let the publisher (`FlexCatalog.Api`) and the consumer
(`FlexCatalog.InventoryProjector`) be two genuinely independent processes,
built and deployed separately, while still agreeing on the schema from one
source of truth -- the same reason a real system publishes to a schema
registry or a shared contracts package rather than letting each side
redefine the JSON shape by convention.

### Fire-and-forget, off the request path

`ProductService.CreateAsync` / `AdjustInventoryAsync` call
`IDomainEventPublisher.Publish(...)` immediately after their MongoDB write
already succeeded. `Publish` is a synchronous, in-memory
`Channel<T>.Writer.TryWrite` call -- no I/O, no `await`, cannot throw for
any reason a caller need handle, and cannot block (the channel is bounded
and configured to drop the oldest pending event rather than block the
writer if a backlog ever built up, which given a single background reader
draining it essentially never happens in this workload). The actual
network call to NATS happens later, on a separate `BackgroundService`
(`DomainEventPublishingService`) that owns its own `NatsConnection` and
drains the channel in a loop, catching and logging every exception (NATS
unreachable, subject rejected, connection reset, etc.) instead of letting
any of them propagate. The result: a NATS outage, a slow NATS, or a
consumer that never shows up are all invisible to `POST /api/products` and
`POST /api/products/{id}/inventory/adjust` -- the HTTP response depends
only on the MongoDB write, exactly as it did before this change. This is
requirement #4 of the task, and it's the reason the publish call sits
*after* the repository call returns successfully, not wrapped around it in
a way that could turn an event-publish failure into a failed HTTP request.

## Consequences

- **At-most-once, no ordering guarantee across restarts, no replay.** If
  `FlexCatalog.InventoryProjector` is down when an event is published, that
  event is gone -- there is no backlog for it to catch up on when it comes
  back. This is the accepted tradeoff of choosing best-effort delivery over
  primary-path risk (see above), not an oversight. A production system that
  needed the projection to always converge regardless of consumer uptime
  would move the subjects onto JetStream streams and switch the projector
  to a durable consumer -- a config-level change on top of the same
  publish/subscribe code, not a rewrite.
- **The projection can lag or (after a missed event) permanently
  under/over-count** relative to MongoDB's `products` collection, which
  remains the single source of truth. The projection is a convenience
  read-model demonstrating the pattern, not a system anything else in this
  repo depends on for correctness -- nothing in the existing REST API reads
  from it.
- **One more moving part in `docker-compose.yml` and CI**: a `nats`
  container, and a second real integration-test suite dependency
  (`Testcontainers.Nats`, mirroring the existing `Testcontainers.MongoDb`
  usage) alongside Mongo. Both images are small and start quickly, so this
  doesn't meaningfully change CI runtime.
- **Tenant isolation and auth are untouched.** Events are stamped with the
  `tenantId` that `ProductRepository` already stamps onto the `Product`
  document from the validated JWT (ADR 0001) -- nothing new reads tenant
  identity from anywhere else, and the projector's Mongo collection is
  separate from `products`, so this doesn't create a second, unscoped path
  to product data.

## Alternatives considered and rejected

- **Kafka**: right tool at real scale; disproportionate operational weight
  for a two-event demo (see above).
- **RabbitMQ / Redis**: both already represent the task-queue pattern
  elsewhere in this portfolio; reusing either here would demonstrate the
  same pattern twice instead of adding the missing one.
- **NATS JetStream instead of core NATS**: rejected for *this* iteration
  specifically because its durability/ack/redelivery semantics are a
  stronger guarantee than requirement #4 asks for on the primary write
  path; documented above as the natural upgrade path if a future consumer
  needs it, tracked in `backlog.md`.
- **In-process `IPublisher`/MediatR-style event dispatch with no broker at
  all**: would demonstrate the observer pattern, not event-streaming
  architecture -- there'd be no independent process, no wire format, no
  network boundary, which is the entire point this task asks to
  demonstrate.
