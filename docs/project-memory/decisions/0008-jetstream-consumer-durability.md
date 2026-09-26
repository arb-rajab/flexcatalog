# ADR 0008: JetStream Durability for the Two Event Consumers

- Status: Accepted
- Date: 2026-09-26

## Context

ADR 0006 chose NATS core pub/sub deliberately -- no persistence, no
acknowledgment, no redelivery -- because that's the honest match for the
*publish* side's requirement: a broker/consumer problem must never fail or
slow down `POST /api/products` or the inventory-adjust endpoint. That
reasoning is correct and this ADR does not reopen it.

But ADR 0006 also named the resulting gap explicitly under "Consequences":
if `FlexCatalog.InventoryProjector` (or, since ADR 0007,
`FlexCatalog.SearchIndexer`) is down when an event publishes, or a handler
throws while processing one, the event is simply gone. Today neither
consumer has any handling for this beyond a `catch` that logs and moves on
(`InventoryProjectionConsumer.ExecuteAsync`,
`SearchIndexingConsumer.ExecuteAsync`) -- a transient MongoDB blip, a
Meilisearch hiccup, or the process being redeployed at the wrong moment all
silently lose the event with no record and no way to recover it. For a
read-model whose whole job is "stay in sync with the event stream," that's
a real gap, not a cosmetic one.

## Decision

**Switch both consumers' *subscriptions* to NATS JetStream durable
consumers. Leave the publish side (`DomainEventPublishingService`) as
plain core NATS `PublishAsync`, completely unchanged.**

This is the option ADR 0006 already flagged as the natural upgrade path
("a config-level change on top of the same publish/subscribe code, not a
rewrite") and it's cheaper than it sounds specifically *because* of that:

- **The publish side needs zero code changes.** JetStream works by
  attaching a *stream* to a subject pattern; any core `PublishAsync` to a
  matching subject is transparently captured into the stream if one
  exists, with no JetStream-aware publish call required. The two
  consumers add a `NatsJSContext`, ensure a stream
  (`FLEXCATALOG_EVENTS`, subjects `flexcatalog.events.>`) exists, and
  create a named *durable* consumer against it instead of calling
  `connection.SubscribeAsync<string>(subject)`. `DomainEventPublishingService`
  never references `NATS.Client.JetStream` at all -- the fire-and-forget,
  can't-fail-the-request guarantee ADR 0006 built is untouched, and there
  was never a scenario in this task's DO NOT list this would have risked.
- **A named durable consumer is exactly "catch up when you come back."**
  Each service registers a durable consumer with a stable name
  (`inventory-projector`, `search-indexer`) and `DeliverPolicy.All`. If
  that process is down for five minutes, the stream keeps every message
  published in that window (bounded by the stream's retention limits, not
  by "did anyone subscribe"); when the process starts back up and resumes
  that same durable name, JetStream resumes exactly where that consumer's
  last acknowledgment left off. This directly answers requirement #2 of
  this task ("prove a consumer that was down during a publish can catch
  up") without inventing a separate replay mechanism -- it's what a durable
  consumer *is*.
- **Explicit ack turns "processing failed" into "redelivered," not
  "gone."** Both consumers already wrap `HandleAsync` in
  try/catch-and-log; the only change is that a caught exception now
  results in `NakAsync()` (redeliver) instead of silently moving to the
  next message, and success results in `AckAsync()`. A transient Mongo/
  Meilisearch failure that would previously have been a permanently lost
  event is now retried automatically by JetStream's redelivery, with the
  same backoff-and-log shape the subscription loop itself already uses
  for broker-unreachable errors.
- **A capped `MaxDeliver` plus a structured dead-letter log covers the
  poison-message case**, so a message that will *never* successfully
  process (a payload that can't deserialize, a bug in a handler) doesn't
  redeliver forever. After `MaxDeliver` (5) attempts, the consumer logs a
  single `LogError` with the event id, subject, delivery count, and the
  envelope's raw JSON, then calls `AckTerminateAsync()` so JetStream stops
  redelivering it. That log line is the "logged/recorded somewhere
  inspectable" this task's scope item 2 asks for -- both processes already
  emit structured logs a container log collector can alert on
  (`docs/project-memory/decisions/0005-observability-and-tenant-scaling-roadmap.md`
  covers the general logging story), so this doesn't invent a second
  storage mechanism just for dead letters. A dedicated dead-letter
  *collection*/stream was considered and rejected below.

### Why not the lighter-weight, logging-only option instead

The task explicitly allowed choosing (b), a pure dead-letter/logging layer
with no durability at all, if that were the better complexity/value
tradeoff. It wasn't, for this specific case:

- The actual gap named in ADR 0006's "Consequences" section is *lost
  events*, not *invisible failures*. A log-only dead letter makes a failed
  *processing attempt* visible, but does nothing for the much more common
  failure mode here -- the consumer process simply wasn't running when the
  event was published (a redeploy, a crash, `docker compose restart`).
  Logging can't retroactively produce an event nobody was subscribed to
  receive; only a broker-side durable backlog can.
- JetStream's actual implementation cost here is small precisely because
  of ADR 0006's own design: one shared subject prefix, an envelope/payload
  schema already agreed via `FlexCatalog.Contracts`, and a subscription
  loop shape (retry-with-backoff around `SubscribeAsync`) that only needs
  its inner loop swapped for `NatsJSConsumer.ConsumeAsync`, not
  restructured. This is not the "Kafka would be disproportionate" case ADR
  0006 already rejected -- JetStream is a mode of the broker already in
  `docker-compose.yml`, not a new service.
- A log-only approach would still need *something* to solve "what happens
  when the consumer is caught up again" -- either a separate replay
  mechanism reading NATS's own JetStream storage anyway (at which point
  you've built half of what this ADR does, worse), or accepting permanent
  data loss on every restart, which is precisely the risk this task was
  opened to close.

The one thing the logging-only option would have avoided -- a new NuGet
dependency (`NATS.Client.JetStream`) and a slightly larger surface in two
`Program.cs` files -- is a small, one-time cost against a gap that
recurs every time either service restarts.

### Why not a dedicated dead-letter *stream/collection*

A `flexcatalog.events.deadletter` stream (or a Mongo
`deadLetterEvents` collection) that poison messages get republished/
persisted into, so they can be listed, requeued, or alerted on
programmatically, was considered. Rejected for this scope: it's a second
storage mechanism duplicating what a structured log line already gives an
operator (searchable, timestamped, alertable via any log collector), for a
failure mode -- a payload that *never* successfully processes after five
attempts -- that in practice means a bug in the mapping code, not an
operational event someone will programmatically requeue. If a future
session needs "list and manually replay dead-lettered events" as an actual
feature, promoting the log line to a real store is a small, additive
change once that need is concrete -- not before.

## Configuration

Both `FlexCatalog.InventoryProjector` and `FlexCatalog.SearchIndexer` gain
two new `Nats` options (`FlexCatalog.InventoryProjector.NatsOptions`,
`FlexCatalog.SearchIndexer.NatsOptions`):

- `StreamName` (default `FLEXCATALOG_EVENTS`) -- shared; either service
  can create the stream if it doesn't exist yet (`CreateOrUpdateStreamAsync`
  is idempotent), so service start order doesn't matter.
- `DurableConsumerName` (default `inventory-projector` /
  `search-indexer` respectively) -- each service's own durable consumer
  name. Distinct names are what let both services independently consume
  every event from the same stream without stealing each other's
  messages (JetStream fans a message out to every distinct durable
  consumer registered against a stream, the same "many independent
  subscribers" semantic ADR 0006 wanted from core pub/sub -- just now
  with a backlog per consumer instead of none).

`MaxDeliver = 5`, `AckWait = 30s`, `DeliverPolicy = All` are fixed in code
rather than configurable -- there's no current need to tune them per
environment, and CLAUDE.md's guidance against speculative configuration
applies.

## Consequences

- **At-least-once delivery, not exactly-once.** A consumer that acks
  successfully but crashes before committing its own side effect (e.g.
  the Mongo upsert succeeds but the process dies before the ack round-trip
  completes) will see that event redelivered on restart. Both consumers'
  handlers are already idempotent upserts/deletes keyed by
  `tenantId`+`productId` (`ReplaceOneAsync` with `IsUpsert`,
  `UpdateOneAsync` with `IsUpsert`, `DeleteOneAsync`, Meilisearch's
  `AddDocumentsAsync`/`UpdateDocumentsAsync` by primary key) -- redelivery
  of an already-applied event is a harmless no-op, not a correctness bug.
  This was true before this change too; it's a property this ADR relies
  on rather than introduces.
- **The stream retains events even with no consumer caught up to them**,
  bounded by JetStream's default limits-based retention (no explicit
  `MaxAge`/`MaxBytes` configured, so the demo relies on JetStream's
  built-in defaults rather than tuning retention for a workload this
  portfolio doesn't actually run at volume -- a real deployment would set
  `MaxAge` to whatever window "how long can a consumer realistically be
  down" needs to cover).
- **`docker-compose.yml`'s `nats` service needs `-js` added** to its
  command args to enable JetStream (it ships disabled by default). No new
  container, no new image -- same `nats:2.10-alpine`.
- **The publish side (ADR 0006's `DomainEventPublishingService`) is
  unchanged, verified by this ADR's own test**: it never references
  JetStream, never checks whether a stream exists, and its behavior when
  NATS is fully unreachable is identical to before this change.
- **CI and integration tests**: `Testcontainers.Nats`'s container needs
  the same `-js` flag; `InventoryEventStreamingTests`/
  `SearchIndexingEventStreamingTests` (ADR 0006/0007) still pass unchanged
  since a durable JetStream consumer behaves the same as a core
  subscriber from the perspective of "does the event eventually land,"
  and this ADR adds a new test file specifically proving the two new
  behaviors (catch-up after downtime, poison-message dead-lettering) that
  those existing tests don't exercise.

## Alternatives considered and rejected

- **Logging-only dead letter, no durability** -- rejected above: doesn't
  solve the actual named gap (events lost while the consumer isn't
  running), only failed-processing visibility.
- **A dedicated dead-letter stream/collection instead of a log line** --
  rejected above as a second storage mechanism for a failure mode that in
  practice means "fix the bug," not "requeue this later."
- **Switching the publish side to JetStream too** (`js.PublishAsync`
  instead of `connection.PublishAsync`) -- explicitly out of scope per
  this task's own DO NOT list, and unnecessary: JetStream captures a
  message published via plain core `PublishAsync` as long as a stream's
  subject filter matches, so the durability gain here doesn't require it.
