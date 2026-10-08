# Dependabot status

_Last updated: 2026-10-08. Maintained during the Dependabot clean-up pass; update when the state changes._

## Configuration

- Ecosystems covered: nuget (`/`), github-actions (`/`), docker (`/`), docker-compose (`/`).
- Grouping: none (one PR per update).
- Schedule: weekly.
- Ignore rules: docker-compose image majors (stateful services need a deliberate migration).

## State at last update

- Open Dependabot PRs: 0 (each merged or closed only after reading its checks).
- Default-branch CI: green at last check.

## Time-limited exemptions

- None.

## Notes

- NATS.Client.* versions must stay aligned across `Api`, `InventoryProjector`, `SearchIndexer` and `IntegrationTests` (NU1605 downgrade error otherwise); Dependabot bumps one project dir at a time, so check the whole solution.
- `SearchIndexingEventStreamingTests` (Meilisearch) is occasionally flaky; one re-run on the same commit is the accepted check.

## Deferred (not re-raised each pass)

- Ignored major versions are listed in `.github/dependabot.yml` with the reason for each.
- Re-check exemptions before their `effectiveUntil` date (2026-11-15) and drop them once upstream fixes ship.
