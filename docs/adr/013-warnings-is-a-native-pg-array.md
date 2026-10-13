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
columns**. `[UNVERIFIED-FOR-EF-10]` — `docs/database.md` marks this as asserted for EF 8.0 and to be
re-checked against the installed provider version before coding; the array mapping has been stable since
8.0 but is written up there for 8.0, not for 10.x.

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
- **This ADR carries an unverified claim.** `docs/database.md` explicitly marks the EF 10 behaviour
  `[UNVERIFIED-FOR-EF-10]`. Do not upgrade it to `[FACT]` without reproducing it against the installed
  `Npgsql.EntityFrameworkCore.PostgreSQL` version. If EF 10 maps it differently (for example as JSON),
  this ADR must be superseded rather than quietly edited.
