# ADR 006 — Partial parse is `Completed` + `Degraded`, never `Failed`

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stated in:** [`docs/scraping.md#partial-results`](../scraping.md#partial-results), [`docs/domain.md#statuses-searchjob`](../domain.md#statuses-searchjob)

## Context

A scrape spans many pages. Some pages may parse and others fail — a selector stops matching, a page
times out, markup shifts. The job can still have produced useful offers.

Losing half the results is not the same as losing the job. An agent that receives 60 valid offers must
not be told "nothing happened", and a `Completed` job with 0 offers must stay distinguishable from a
scrape that failed — distinguishing "no matches" from "scraping failed" is the whole reason `Failed` and
`Completed` are separate statuses.

## Decision

If some pages parsed and some failed, the job is **`Completed`** with **`Degraded = true`**, and
`Warnings` lists what failed.

- A page failure is retried up to the per-page limit **before** it is counted as failed.
- `Warnings` is the record of what was lost.
- `OffersCount` is the truth the agent sees; it never has to guess completeness.
- Partial parse does not produce `Failed`. `Failed` is reserved for a job that produced nothing usable
  or hit a terminal condition (for example `ANTI_BOT_DETECTED`, see [ADR 014](./014-honest-ua-no-evasion.md)).

| Outcome | Status | `Degraded` | `Warnings` |
|---|---|---|---|
| all pages parsed | `Completed` | `false` | empty |
| some pages failed | `Completed` | `true` | list of what failed |
| no page parsed / terminal block | `Failed` | n/a | n/a |

## Consequences

- The agent always gets an answer, and always knows how complete it is. A degraded result is
  actionable; a `Failed` job is not.
- `Degraded` and `Warnings` are first-class fields the MCP contract must expose, and partial success
  must not be reported to the agent as failure.
- Logging levels follow: a degraded job logs at `warn` (degraded but handled), not `error`.
- Monitoring must count `Degraded` separately from `Failed`. A rising `Degraded` rate is the early
  signal that the source markup changed, well before jobs start failing outright.
- Retry policy is per page, not per job — so the per-page limit is part of the design, not a detail.
