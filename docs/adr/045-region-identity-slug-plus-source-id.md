# ADR 045 — A region's identity is a slug plus the source's id

- **Status:** Accepted
- **Date:** 2026-10-15
- **Stated in:** [`docs/domain.md#region-catalog`](../domain.md#region-catalog), [`docs/scraping.md#selectors`](../scraping.md#selectors)
- **Related:** [ADR 010](./010-list-regions-prevents-invented-regions.md), [ADR 011](./011-regions-are-a-catalog-entity.md), [ADR 039](./039-region-delete-restrict.md), [ADR 046](./046-region-catalog-seeded-by-migration.md)

## Context

`Region` was defined with four fields — `Id`, `Label`, `Source`, `Active` — before any real region data
existed. Collecting the actual catalog from TourVisor (`[FACT]`, 2026-10-15) showed that the source
identifies a region two ways that the entity could not represent:

- a **numeric id** (`22` for Кемер, `277` for Стамбул), which the site's own search uses:
  `tourvisor.ru/search.php?region=22`;
- **nesting** via a `parentid` field — Турция has 85 regions: 20 parents and 65 children
  (Аланья → Авсаллар, Инжекум, …).

Two candidate designs were rejected:

- **Use the numeric id as `Region.Id`.** It matches the source exactly and needs no mapping, but `Id` is
  what the agent sends back in `search_tours.region` and what a human reads in logs and error messages.
  A bare `"22"` is indistinguishable from a typo, and it makes the agent's identifier depend on the
  source's internal numbering.
- **Reuse `Region.Source` for the numeric id.** `Source` already means *which source this region belongs
  to* (`"tourvisor"`) — ADR 011 relies on that meaning to make a second source a data change. Overloading
  it would silently redefine an existing field, which is exactly the kind of change that breaks readers
  who never see this file.

## Decision

A region carries **three** identifiers, each with one job:

| Field | Example | Job |
|---|---|---|
| `Id` | `"kemer"` | **slug PK** — the value the agent passes to `search_tours.region` and reads in logs |
| `SourceId` | `"22"` | the source's own numeric id, used to build `?region={SourceId}` |
| `ParentId` | `"alanya"` or `null` | the **parent's `Region.Id`** — a self-referencing FK |
| `Source` | `"tourvisor"` | unchanged: which source this is |

**Slug rule.** Lowercase, Cyrillic→Latin transliteration, spaces and ` - ` → `-` (so
`"Кемер - центр"` → `kemer-tsentr`). Two rules refine it, both discovered in the real data:

1. **Collision with the parent is qualified by the parent's slug.** `Бурса` (parent 268) and `Бурса`
   (child 2517) both transliterate to `bursa`; likewise `Кайсери`. The child becomes **`bursa-bursa`** /
   **`kayseri-kayseri`**. This affects exactly 2 of 85 rows.
   - Rejected: a `-2` suffix (`bursa-2`) — it encodes arrival order, so it would change if the source
     reorders, breaking a stored id for no reason.
   - Rejected: parent-qualifying **all** children (`kemer-tekirova`) — it degrades 63 correct slugs to fix
     2, turning `Кемер - центр` into `kemer-kemer-tsentr`.
2. **A small allow-list of conventional exonyms.** `Стамбул` → **`istanbul`**, not the mechanical
   `stambul`. This is legitimate only because the slug is **never sent to the source** — `SourceId`
   carries the numeric id into the URL — so the slug is purely an agent- and human-facing name, and the
   recognised English name serves both readers better than a transliteration artefact. The list is
   deliberately bounded: it is exactly `{Стамбул → istanbul}` today. Mechanical transliteration remains
   the default; adding an exonym requires a reason, not a preference.

`SourceId` is unique per source: a unique index on `(source, source_id)`. `ParentId` is a self-referencing
FK with `DeleteBehavior.Restrict`, consistent with [ADR 039](./039-region-delete-restrict.md) — the catalog
is deactivated, never deleted.

## Consequences

- The agent's vocabulary is stable and readable, and the parser's URL is exact, with no mapping table in
  between. Each field has one reason to change.
- **The slug must never be sent to the source.** Reaching for `Region.Id` where `SourceId` is meant
  produces a URL the source does not understand — a silent zero-result, the failure ADR 010 exists to
  prevent. The `url_template` uses `{region_id}` meaning `SourceId`.
- If the source renumbers a region, only `SourceId` changes; a slug already handed to an agent keeps
  working. That is the point of keeping them separate.
- **The seed rules are now data rules.** A future pull that introduces a new parent/child slug collision
  is handled by rule 1 automatically; a new *desired* exonym needs a deliberate edit and a note here.
- **A uniqueness check must span the whole key space.** The first verification of the slug rule tested
  parents and children *separately* and therefore reported "no collisions" for 85 rows that produce only
  83 unique slugs. Recorded because the failed check looked conclusive.
- The `(source, source_id)` index means two regions cannot claim one source id — a data-integrity bug
  that would otherwise surface as offers attached to the wrong region.
- `Region` now has 6 fields; `docs/domain.md` and `EntityShapeTests` are updated together, so the shape
  cannot drift from the documentation unnoticed.
