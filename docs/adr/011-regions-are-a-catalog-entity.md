# ADR 011 — Regions are a catalog entity; `TourOffer.RegionId` is a foreign key

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stated in:** [`docs/domain.md#region-catalog`](../domain.md#region-catalog), [`docs/mcp-tools.md`](../mcp-tools.md)

## Context

`list_regions` ([ADR 010](./010-list-regions-prevents-invented-regions.md)) needs a region list, and the
scraper needs the same list to build its requests. Two bad options were available:

- Build the region list from scraped data on demand — it is unavailable before the first scrape, which
  is exactly when the tool is needed.
- Hardcode the list in two places (the tool and the scraper) — the two copies drift, and a region the
  scraper knows about becomes unmatchable by the agent.

## Decision

Regions are a **catalog entity**, `Region`, with `TourOffer.RegionId` as a real foreign key.

| Entity | Field | Notes |
|---|---|---|
| `Region` | `Id` | `string` PK, slug — the value the agent passes back as `search_tours.region` |
| | `Label` | `string` display name |
| | `Source` | `string` source id; one source per milestone today, kept so a second source is a data change |
| | `Active` | `bool` — a region the source stopped selling is **deactivated, not deleted** |

- Seeded at startup from the same config the scraper reads (`selectors.json` region list) — **one source
  of truth** for region names.
- `TourOffer.RegionId` is a foreign key to `Region`, so a region is a real entity rather than a bare
  string on the offer.
- `offers_count` is computed from `tour_offers` through the `region` index, not stored.

## Consequences

- The agent's region vocabulary and the scraper's are the same list by construction — drift is
  impossible, because there is one seed.
- A new source becomes a **data change** (add a `Source`), not a schema or code change.
- Regions are never deleted: deactivation preserves historical offers whose `RegionId` would otherwise
  dangle. Deleting a region would need a cascade decision, and that is avoided entirely.
- Startup must fail fast if the region set is unknown or empty
  ([`docs/runtime.md#startup`](../runtime.md#startup)), because an empty catalog makes every
  `search_tours` call succeed with 0 offers.
- The FK means seeding must run before scraping; an offer referencing an unseeded region is a startup
  ordering bug, not a data-quality issue.
