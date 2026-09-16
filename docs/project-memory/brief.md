# Project Brief

## What this is

FlexCatalog is a multi-tenant product catalog and inventory API built on
ASP.NET Core (.NET 10) and MongoDB. It's a portfolio skill-demo aimed
squarely at the .NET + NoSQL gap in an otherwise SQL/relational-heavy set
of projects: the business scope (a product catalog) is intentionally
simple, so the engineering -- tenant isolation, schema design, indexing,
faceted search, testing, CI, docs -- can carry the weight.

## Why a product catalog, and why MongoDB

A catalog with categories that have genuinely different attribute shapes
(electronics specs vs. apparel sizes/colors vs. book metadata) is an
honest reason to reach for a document database instead of a relational
one -- see ADR 0002 for the full reasoning, but in short: a relational
schema for this either grows a wide table of mostly-NULL columns, falls
back to an EAV side table, or reinvents a document store inside a JSONB
column. MongoDB's native polymorphic document support and its aggregation
framework (used for faceted search, ADR 0004) let that flexibility show
up directly in the code rather than being asserted in prose.

## Who this is for

Primary audience: anyone reviewing this portfolio to assess .NET and
MongoDB engineering ability -- expect them to read the code, the ADRs,
and the tests, not just run the API. Secondary: the author, as a
reference implementation of the multi-tenant-isolation and
faceted-search patterns for future projects.

## Scope

In scope:
- Multi-tenant product catalog (Electronics / Apparel / Books) with
  category-specific attributes
- Tenant-isolated CRUD + inventory adjustment
- Faceted search (category, price range, in-stock, free text,
  category-specific attribute filters) via MongoDB aggregation
- JWT authentication, Admin/Viewer roles
- Docker (multi-stage, non-root) + docker-compose (API + MongoDB)
- CI: build, lint (format), unit tests, integration tests (Testcontainers
  + real MongoDB), Docker image build

Out of scope (see `backlog.md` for what's deferred and why):
- Payments, orders, checkout -- this is a catalog/inventory system, not a
  storefront
- Multi-currency conversion (currency is stored per-product as a label,
  not converted)
- Per-resource ACLs beyond Admin/Viewer
- Independent-branch faceted navigation (ADR 0004)
- Token revocation / refresh tokens
- Real external IdP integration
