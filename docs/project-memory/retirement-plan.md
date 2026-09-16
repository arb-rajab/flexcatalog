# Retirement Plan

This is a portfolio skill-demo, not a production system with real
tenants or real data, but it's documented as if it had to be retired
responsibly, matching the discipline used across the rest of this
portfolio.

## Trigger conditions

Retire (archive, stop maintaining) this repository if any of:
- It's superseded by a newer demo covering the same .NET + MongoDB +
  multi-tenancy ground more effectively.
- The portfolio it belongs to is reorganized such that a standalone
  catalog/inventory demo no longer adds distinct signal.
- Dependencies (`.NET`, `MongoDB.Driver`, `MongoDB` server itself) age out
  far enough that keeping CI green stops being worth the effort relative
  to the demo's value.

## Data

No real user or customer data exists in this system -- only seeded demo
data (`DataSeeder`, two fictional tenants, `*.test` email addresses, made
up product SKUs). Retirement requires no data export, no customer
notification, and no data-deletion compliance process. If this were ever
repurposed to hold real data, that assumption would need to be revisited
explicitly before doing so.

## Steps to retire

1. Confirm no other repository or document in the portfolio links to this
   one as a live dependency (it has none currently -- it's a standalone
   demo with no consumers).
2. Set the GitHub repository to archived (read-only) rather than deleting
   it -- preserves the commit history, PR, and CI record as evidence of
   the work for portfolio purposes.
3. Update the top-level portfolio index (if/when one exists) to mark this
   entry as archived rather than removing the reference outright.
4. No infrastructure to tear down -- this repo defines no cloud resources
   of its own; anyone who deployed it themselves (e.g. via the Dockerfile
   to their own environment) is responsible for tearing down that
   deployment independently.

## What would NOT trigger retirement

- CI going red on a dependency bump -- that's a maintenance task (fix and
  keep CI green), not a reason to retire.
- The demo's contrived business scope being "boring" -- the scope is
  deliberately simple by design (`brief.md`); the engineering underneath
  it is the point.
