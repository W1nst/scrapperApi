# ADR 013 — `Warnings` is a native PostgreSQL array column, not a child table

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stated in:** [`docs/database.md#warnings-column-representation`](../database.md#warnings-column-representation), [`docs/scraping.md#partial-results`](../scraping.md#partial-results)

## Context

Partial parse ([ADR 006](./006-partial-parse-is-completed-degraded.md)) needs `Warnings` on `SearchJob` —
a list of what failed during a job. Two shapes were possible: a child table
(`search_job_warnings(job_id, message)`), or a column on the job.

`Warnings` is a short, always-read-with-the-job, never-queried-by-content list. A child table adds a
join, a cascade rule, and a second entity, to serve data that is only ever rendered alongside its
parent.

EF Core 8+ maps primitive collections automatically, and on Npgsql they map to **native PostgreSQL array
columns**. `[FACT]` **verified by reproduction on 2026-10-13** against the pinned stack
(`Microsoft.EntityFrameworkCore` 10.0.12, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3): the generated
DDL contains `warnings text[] NOT NULL`, and the property's column type reads back as `text[]` with CLR
type `List<string>`. The earlier `[UNVERIFIED-FOR-EF-10]` marker is closed — the mapping holds on EF 10,
not only 8.x.

## Decision

`Warnings` is a **native PostgreSQL array column** (`text[]`) on `search_jobs`. No child table.

- `List<string> Warnings` requires no extra configuration — EF Core maps primitive collections
  automatically, and Npgsql produces `text[]`.
- Empty array, not `null`, when a job is not degraded.

## Consequences

- No join, no cascade rule, no second entity — the job is read in one query.
- The column is render-only. It is **not** indexed and not queried by content. If a future feature needs
  "find all jobs with warning X", that is a new requirement and may justify revisiting this decision —
  but it would then need its own ADR, not a silent schema change.
- Empty-array-not-null keeps `Warnings` total: consumers never branch on null.
- **This ADR's central claim is now verified, not asserted.** It was previously marked
  `[UNVERIFIED-FOR-EF-10]`; it has been reproduced against the pinned stack and the marker is closed.
  If a future EF or Npgsql version maps the collection differently (for example as JSON), this ADR must
  be superseded rather than quietly edited.
