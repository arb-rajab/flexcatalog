# Dependabot status

_Last updated: 2026-10-09. Maintained during the Dependabot clean-up pass; update when the state changes._

## Configuration

- Ecosystems covered: nuget (`/`), github-actions (`/`), docker (`/`), docker-compose (`/`).
- Grouping: none (one PR per update).
- Schedule: weekly.
- Ignore rules: docker-compose image majors (stateful services need a deliberate migration).

## State at last update

- Open Dependabot PRs: 0 (each merged or closed only after reading its checks).
- Default-branch CI: green at last check.
- Last full rescan: 2026-10-09. Checked open PRs (none), default-branch and scheduled CI, Dependabot update jobs, ecosystem coverage (no new manifests since 2026-10-08), Actions pins, exemption expiry dates and stray branches, plus three new dimensions: branch-protection required contexts against the check runs a PR actually produces, the repo's `security_and_analysis` settings, and check-run annotations on `main`. No required context is stale. The annotations showed `ubuntu-latest` moving to Ubuntu 26 from 2026-10-19, so every job is now pinned to `ubuntu-24.04` (see Notes). The full-history gitleaks scan was not repeated: the only commits since 2026-10-08 are docs and CI changes, each scanned by the push-run gitleaks job. Rescan cycle 2 (same day, after those pins merged) repeated every dimension and added one: each repo's `SECURITY.md` and whether GitHub private vulnerability reporting is enabled.
- Rescan cycle 3 (2026-10-09) repeated every dimension and added three: fork-PR workflow approval, whether `main` requires branches to be up to date before merging (`strict`), and the extra secret-scanning settings (non-provider patterns, validity checks). Live result: no open PRs; `security_and_analysis` shows secret scanning, push protection and Dependabot security updates enabled; private vulnerability reporting enabled; every required context is produced by the last merged PR's check runs and none is missing. The repo owner's token read 0 open Dependabot, code-scanning and secret-scanning alerts the same day (the first secret-scanning read; that permission was added to the token on 2026-10-09).
- Rescan cycle 4 (2026-10-09, after the cycle-3 docs PRs merged) repeated every dimension and added two: open issues (none) and the push-run CI on `main` after those merges (green). Still no open PRs, all `security_and_analysis` settings above still enabled, private vulnerability reporting still on, `strict` on, `main` the only branch, and no required context missing from the merged PR's check runs. No new gaps.

## Time-limited exemptions

- None.

## Notes

- NATS.Client.* versions must stay aligned across `Api`, `InventoryProjector`, `SearchIndexer` and `IntegrationTests` (NU1605 downgrade error otherwise); Dependabot bumps one project dir at a time, so check the whole solution.
- `SearchIndexingEventStreamingTests` (Meilisearch) is occasionally flaky; one re-run on the same commit is the accepted check.
- Merge policy (deliberate choice by the repo owner, 2026-10-08): every PR, major-version dependency bumps included, is merged as soon as all of its required checks are green, confirmed per PR. This repo is a code showcase with no business or sensitive dependency, so green checks are the only gate. Red, pending or conflicted PRs are fixed or closed instead.
- Every Linux job runs on `ubuntu-24.04` (pinned 2026-10-09; it is what `ubuntu-latest` resolved to). GitHub moves `ubuntu-latest` to Ubuntu 26 from 2026-10-19, and an unattended image change could turn every check red at once. Move to `ubuntu-26.04` deliberately, in one PR whose CI has run on it. Dependabot does not bump `runs-on` labels.
- `SECURITY.md` added 2026-10-09 (rescan cycle 2: the repo had no security policy GitHub recognises; `docs/project-memory/security.md` is design notes). It sends reporters to GitHub private vulnerability reporting, which was disabled here; the repo owner changed it through the API on 2026-10-09 and the read-back confirmed it (`enabled: true`).
- Fork-PR workflow approval is `all_external_contributors` (set by the repo owner through the API on 2026-10-09, PUT 204, read back with the owner's token because the session's proxy blocks Actions paths).
- Secret scanning for non-provider patterns and validity checks stay off: the owner's PATCH on 2026-10-09 returned 200, but the read-back still shows both `disabled`, so GitHub does not offer them on this user-owned public repo. Not retried.
- No `LICENSE` file; it is the only one of the 12 repos without one (the others use MIT, AGPL-3.0 or all-rights-reserved). Picking a licence is the repo owner's call, so it is left to them and not re-raised.

## Deferred (not re-raised each pass)

- Ignored major versions are listed in `.github/dependabot.yml` with the reason for each.
- Re-check exemptions before their `effectiveUntil` date (2026-11-15) and drop them once upstream fixes ship.
- Alerts read 2026-10-09 with the repo owner's PAT, run on their machine (Claude sessions still get 403: the proxy sends a GitHub App token instead of `GH_ALERTS_TOKEN`, even a PAT passed explicitly). No open Dependabot or code-scanning alerts. Re-read 2026-10-09 after rescan cycle 2: still no open alerts.
