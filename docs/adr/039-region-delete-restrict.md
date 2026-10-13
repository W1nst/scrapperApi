# ADR 039 — `TourOffer.RegionId` restricts deletes; it does not cascade

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stated in:** [`docs/domain.md#region-catalog`](../domain.md), [`docs/database.md#cleanup-ttl`](../database.md)

## Context

Two foreign keys leave `tour_offers`, and they need **opposite** delete behaviour:

- `SearchJobId → search_jobs` — documented as `Cascade`: offers follow their job, because an offer without
  its job is meaningless.
- `RegionId → regions` — a region is **deactivated (`Active = false`), never deleted**, because offers
  reference it and history must survive.

`[FACT]` verified by reproduction on EF Core 10.0.12 + Npgsql 10.0.3: a required reference left
unconfigured defaults to **`DeleteBehavior.Cascade`**, and the generated DDL was
`... REFERENCES tour_search.regions (id) ON DELETE CASCADE`. The specification was silent on this FK, so
the default would have shipped: deleting one region would have silently deleted every offer in it — the
exact outcome `docs/domain.md#region-catalog` exists to prevent.

## Decision

`TourOffer.RegionId → Region` is configured **`OnDelete(DeleteBehavior.Restrict)`**, explicitly.

`TourOffer.SearchJobId → SearchJob` stays `Cascade`, per `docs/database.md#cleanup-ttl`.

| FK | Behaviour | Why |
|---|---|---|
| `tour_offers.search_job_id → search_jobs.id` | `Cascade` | offers are owned by their job; the TTL cleanup relies on it |
| `tour_offers.region_id → regions.id` | **`Restrict`** | regions are deactivated, not deleted; history must survive |

## Consequences

- The database now **enforces** the catalog rule instead of leaving it to convention. Deleting a region
  that still has offers fails loudly at the constraint, rather than quietly destroying data.
- Deactivating a region (`Active = false`) is a plain `UPDATE` and is unaffected — which is the intended
  way to retire a region.
- Deleting a region with no offers still works, so a genuine cleanup of an unused slug is not blocked.
- Because the behaviour is written explicitly, it survives a future EF default change and is visible to
  a reviewer who never runs the code.
- This must be asserted, not assumed: a test walks `context.Model` and checks both delete behaviours
  exactly, so a regression is caught without a database.
- `Cascade` and `Restrict` sit on sibling FKs of the same table, which is unusual enough to look like an
  oversight to a reader. This ADR is the explanation, and the code carries a comment pointing here.
