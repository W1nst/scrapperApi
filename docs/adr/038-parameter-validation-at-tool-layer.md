# ADR 038 — Parameter validation lives at the MCP tool layer

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stated in:** [`docs/domain.md#error-taxonomy`](../domain.md), [`docs/mcp-tools.md#errors--two-different-layers`](../mcp-tools.md)

## Context

`docs/domain.md` documents value ranges for search parameters — `Adults ≥ 1`, `Children ≥ 0`,
`HotelStars` in `1..5` or `0` for any, `Nights ≥ 1`, `PriceMax ≥ PriceMin`. Nothing in the specification
says **where** those ranges are enforced. Three plausible homes existed:

- domain invariants in the entities (`IValidatableObject`, constructor checks, a factory);
- database `CHECK` constraints;
- the MCP tool layer, at the boundary.

The stakes are real: a malformed parameter set produces a `Completed` job with 0 offers, which is
**indistinguishable from a genuinely empty result** — the same failure mode that motivates `list_regions`
([ADR 010](./010-list-regions-prevents-invented-regions.md)).

## Decision

Parameter ranges are enforced at the **MCP tool layer**, which rejects them with `PARAM_INVALID` before a
job exists.

- The entities in `TourSearch.Core` carry **no** validation logic and no `IValidatableObject`.
- The database carries **no** `CHECK` constraints for these ranges.
- `docs/domain.md#error-taxonomy` already defines `PARAM_INVALID` as "rejected before the job exists —
  tool-level only, **never persisted**", which is exactly the contract this implements.
- No check-constraints are specified anywhere in the specification, so adding them here would be a new
  decision made in passing rather than a recorded one.

## Consequences

> **Update 2026-10-15 — implemented.** The deferred obligation is discharged: task `006` enforces every
> range in the `search_tours` tool layer, returns `PARAM_INVALID` as a **tool-level** error
> (`isError: true` + `structuredContent` naming the offending `field`), persists nothing on rejection, and
> adds no entity validation and no `CHECK` constraint — exactly as decided below. The trigger for
> revisiting is unchanged and still not met: no second writer of `SearchJob` exists.

- The invalid input never becomes a job, so it never becomes a confusing `Completed`-with-0-offers
  result. The caller gets a structured error naming the offending field, which a `CHECK` constraint
  cannot express.
- The failure is reported at the layer that has the parameter names as the caller wrote them, so the
  message is directly actionable.
- **Accepted risk, recorded rather than hidden: invalid data can still be written by direct SQL or by a
  future second writer, and no layer will stop it.** Today `search_tours` is the only writer — there is no
  admin UI, no import path, no other entry point — so the tool layer is a complete chokepoint. That
  property is what makes this decision sound, and it is a property of the current design, not a guarantee.
- **Trigger for revisiting:** the moment a second writer of `SearchJob` appears (a seeding script, an
  internal API, a data-import job), check-constraints or domain invariants become necessary. That change
  requires a new ADR superseding this one, not a silent addition.
- Tests for the ranges belong to the tool layer, where they can assert the returned `PARAM_INVALID`
  payload — not to the entity tests.
