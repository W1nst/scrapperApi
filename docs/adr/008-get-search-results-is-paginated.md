# ADR 008 — `get_search_results` is paginated, never unbounded

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stated in:** [`docs/mcp-tools.md#pagination--mandatory`](../mcp-tools.md#pagination--mandatory)

## Context

A scraping job can produce hundreds of offers. A single unbounded response returns all of them into an
agent's context window, which is the most expensive failure mode in this system: the response is not
slow, it is unusable, and its cost is paid by the caller.

Offset pagination is also wrong here, because a job may still be scraping while the agent pages: new
offers are inserted, so an offset over a growing result set shifts and the agent sees duplicates or
skips rows.

## Decision

`get_search_results` **must never** return the whole offer list.

- `limit` — default `20`, maximum `50`.
- Cursor is **opaque** (`next_cursor`), not an offset.
- Ordering matches the sort the page is served in (see
  [ADR 009](./009-offer-cursor-is-rank-order.md)).
- A rank cursor stays stable while a job is still scraping.
- The agent is expected to ask for the top-N, not to page through everything.

## Consequences

- No unbounded list can enter an agent context through this tool. The `limit` cap is enforced
  server-side; a client-supplied larger value is clamped or rejected, not honoured.
- Responses are bounded in size and therefore in cost, which also bounds our own serialization cost.
- The cursor must encode the ordering it was issued for. If the sort ever becomes caller-selectable, the
  cursor must carry the sort — otherwise a cursor issued for one ordering is silently applied to
  another and returns wrong rows. That possibility is currently `[OPEN]` in
  `docs/mcp-tools.md#pagination--mandatory`.
- `offers_total` is the single authoritative count (`= OffersCount`); there is no second count inside
  `page`. One number, one name — a second count would eventually disagree with the first.
- Any new MCP tool that returns a collection inherits this rule. Pagination is the default, and an
  unpaginated list needs its own justification.
