# ADR 009 — The offer cursor is rank order `(price, hotel_stars, id)`, not `(created_at, id)`

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stated in:** [`docs/mcp-tools.md#pagination--mandatory`](../mcp-tools.md#pagination--mandatory), [`docs/domain.md#guid-ordering`](../domain.md#guid-ordering)

## Context

Two different cursors exist in the system, and mixing them is a contract bug.

`TourOffer` has **no `CreatedAt` field at all** — its timestamp is `ScrapedAt`. So a `(created_at, id)`
cursor cannot be applied to offers even in principle. That cursor belongs to **job** listing.

A rank cursor also stays stable while a job is still scraping (new offers are inserted, not appended at
an offset), which is exactly the property offset pagination lacks and
[ADR 008](./008-get-search-results-is-paginated.md) requires.

## Decision

Offers are returned in server-rank order, and the cursor encodes that order:

- `price` **ascending**, then
- `hotel_stars` **descending**, then
- `id` as a stable tiebreaker.

Cursor = `(price, hotel_stars, id)`.

The `(created_at, id)` cursor in [`docs/domain.md#guid-ordering`](../domain.md#guid-ordering) is for
**job** listing (`list_recent_searches`). It is **not** the offer cursor.

| Cursor | Applies to | Fields |
|---|---|---|
| rank | `get_search_results` offers | `(price, hotel_stars, id)` |
| chronological | `list_recent_searches` jobs | `(created_at, id)` |

A v4 UUID has no ordering, so `ORDER BY id` and offset pagination over it are meaningless and unstable
— hence `id` appears only as a final tiebreaker, never as the primary sort.

## Consequences

- The ordering must match the sort the page is actually served in, otherwise the cursor's position
  references a rank that does not exist in the response and paging silently skips or duplicates rows.
- The supporting index on `tour_offers` must match `(price, hotel_stars, id)`; a mismatched index means
  a sort on every page.
- `TourOffer` must not gain a `CreatedAt` "for convenience" — its absence is what makes the two cursors
  impossible to confuse.
- If the sort ever becomes caller-selectable, the cursor must encode the sort it was issued for; a
  cursor from one ordering applied to another returns wrong rows.
- `[OPEN]` whether the agent may request a different sort. Until that is decided, the rank order above
  is the only supported one.
