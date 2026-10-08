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
- Last full rescan: 2026-10-08. Checked open PRs, default-branch and scheduled CI, Dependabot update jobs, ecosystem coverage against the manifests in the repo, Actions pins, exemption expiry dates, stray branches, and (new this pass) a local full-history gitleaks 8.28.0 scan. No new gaps.

## Time-limited exemptions

- None.

## Notes

- NATS.Client.* versions must stay aligned across `Api`, `InventoryProjector`, `SearchIndexer` and `IntegrationTests` (NU1605 downgrade error otherwise); Dependabot bumps one project dir at a time, so check the whole solution.
- `SearchIndexingEventStreamingTests` (Meilisearch) is occasionally flaky; one re-run on the same commit is the accepted check.
- Merge policy (deliberate choice by the repo owner, 2026-10-08): every PR, major-version dependency bumps included, is merged as soon as all of its required checks are green, confirmed per PR. This repo is a code showcase with no business or sensitive dependency, so green checks are the only gate. Red, pending or conflicted PRs are fixed or closed instead.

## Deferred (not re-raised each pass)

- Ignored major versions are listed in `.github/dependabot.yml` with the reason for each.
- Re-check exemptions before their `effectiveUntil` date (2026-11-15) and drop them once upstream fixes ship.
- Dependabot/code-scanning alert API (2026-10-08): not readable. The proxy-injected `GH_ALERTS_TOKEN` is sent, but `GET /repos/arb-rajab/*/dependabot/alerts` and `/code-scanning/alerts` return 403 "Resource not accessible by integration" on all 12 repos; the token lacks the `vulnerability_alerts` / `security_events` read permissions. Alert state remains unverified.
