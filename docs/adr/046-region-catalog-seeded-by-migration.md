# ADR 046 — The region catalog is seeded by migration from committed data

- **Status:** Accepted
- **Date:** 2026-10-15
- **Stated in:** [`docs/domain.md#region-catalog`](../domain.md#region-catalog), [`docs/database.md#migrations`](../database.md#migrations)
- **Related:** [ADR 011](./011-regions-are-a-catalog-entity.md), [ADR 041](./041-no-automatic-migration-on-startup.md), [ADR 044](./044-tools-ship-as-stubs-first.md), [ADR 045](./045-region-identity-slug-plus-source-id.md)

## Context

`docs/domain.md#region-catalog` originally said the catalog is *"seeded at startup from the same config the
scraper reads (`selectors.json` region list)"*. That sentence was written before any region data existed,
and three things make it the wrong mechanism:

1. **Startup seeding contradicts ADR 041's spirit.** That ADR established that application startup must
   never change database state: two processes (`Api`, `Worker`) would race, and a schema/data change
   becomes invisible — an outage instead of a reviewable step.
2. **It would make the application depend on a scraping endpoint.** The catalog comes from
   `tourvisor.ru/xml/listdev.php?...&format=json`. Fetching it at startup reintroduces exactly the runtime
   dependency on the source that the catalog entity exists to remove (ADR 011), and it would add an
   external call — and a failure mode — to every start.
3. **`selectors.json` would become a runtime input.** The file is human-editable config. If a process read
   it at startup, an edit would silently rewrite production data rather than passing through review.

At the same time, keeping the data **only** in `selectors.json` is also insufficient: `list_regions` reads
the database, and a tool that queries a table nothing populates is the empty-catalog failure ADR 010
exists to prevent.

## Decision

The catalog is **collected once, committed, and applied by migration**.

- `selectors.json` (repo root) is the **human-editable source**: `schema_version`, `source`, `updated_at`,
  and `regions[]` with `id`, `source_id`, `label`, and optional `parent_id`. It is validated against a
  minimal JSON Schema at startup — a malformed file fails startup rather than the first job
  (`docs/scraping.md#selectors`).
- The migration **embeds the seed data as C#** and applies it with
  `INSERT ... ON CONFLICT (id) DO UPDATE`. It must **not** read `selectors.json` at runtime: an applied
  migration has to be reproducible without the working tree's config.
- **Nothing seeds at startup.** 85 rows exist because a migration ran, exactly like the schema.
- Re-applying the migration corrects a changed label instead of failing; a region whose label the source
  renames keeps its slug, so an id already handed to an agent stays valid.
- The site is read **during planning only**, once, with an honest User-Agent, against an endpoint that
  requires no authentication and no evasion ([ADR 014](./014-honest-ua-no-evasion.md)). No test, build or
  CI step may contact it (`AGENTS.md` §3).

The duplication is deliberate and made safe: the mapping exists in `selectors.json` and in the migration,
and a **test asserts they agree**, so editing the file without a migration fails the build instead of
silently serving stale names.

## Consequences

- Deployment has no ordering hazard beyond the existing one: migrate, then start — the same rule as the
  schema (ADR 041).
- The application makes **no** network call to TourVisor at startup or during a `list_regions` call. A
  region list that cannot be fetched does not stop the API from starting.
- Updating the catalog is a **reviewable two-file change** (config + migration) with a test enforcing
  agreement, not an implicit side effect of a restart.
- Adding a region is a data change (ADR 011) expressed as a migration; adding a second **source** stays a
  data change too, because `Source` and `SourceId` are already on the row.
- The `updated_at` in `selectors.json` records when the data was pulled, so a reviewer can see the
  catalog's age without querying the source.
- **The cost is a rebuild to refresh the catalog.** A region added by the source is invisible until
  someone re-runs the collection and applies a migration. That is accepted: the alternative is a runtime
  dependency on a scraping endpoint, and freshness of the catalog is not on any critical path.
- **This supersedes the "seeded at startup" sentence** in `docs/domain.md`, which is corrected rather than
  left to contradict the code.
