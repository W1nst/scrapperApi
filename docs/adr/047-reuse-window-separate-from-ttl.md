# ADR 047 — The reuse window is separate from the job TTL

- **Status:** Accepted
- **Date:** 2026-10-15
- **Stated in:** [`docs/domain.md#identity-and-dedupe`](../domain.md#identity-and-dedupe), [`docs/database.md#cleanup-ttl`](../database.md#cleanup-ttl)
- **Related:** [ADR 012](./012-no-idempotency-key.md), [ADR 046](./046-region-catalog-seeded-by-migration.md)

## Context

`docs/domain.md#identity-and-dedupe` said two agents asking the same parameters *"within the TTL window
reuse the existing `Completed` job"*. The only TTL in the specification is the **30-day** job retention
(default, `docs/database.md#cleanup-ttl`).

Those are two different questions being answered with one number:

- **Retention** — how long the row is worth *keeping*. Long, because deleting it early loses history and
  breaks the offer FK. 30 days is reasonable for that.
- **Freshness** — how long a result is worth *presenting as an answer*. Short, because tour prices move.

Using retention as the freshness window means a `Completed` job from 29 days ago is handed back as an
answer to a new request, with `reused: true`. The caller cannot distinguish that from a search run a
minute ago: the response fields are identical, and the agent has no reason to suspect the data is stale.
That is **silent misinformation** — the same failure class as `list_regions` returning an empty catalog
(ADR 010), where a wrong answer is indistinguishable from a right one.

## Decision

Reuse is governed by a **reuse window**, configurable, default **24 hours** — deliberately not the 30-day
TTL.

| Question | Mechanism | Default |
|---|---|---|
| How long is the row kept? | TTL cleanup (`docs/database.md#cleanup-ttl`) | 30 days |
| How long is a result fresh enough to reuse? | reuse window (`Search:ReuseWindowHours`) | 24 hours |

- Only a `Completed` job whose `CompletedAt` falls inside the window is reused, and the **newest** eligible
  one wins.
- Outside the window, a **new** job is created. The old row is untouched — retention and reuse do not
  interfere.
- `docs/domain.md` is corrected: the two are named and defined separately.

## Consequences

- An agent never receives a months-old result presented as today's. The window bounds how wrong a
  `reused: true` answer can be.
- The window is a **tuning knob**, not a constant: it belongs next to the worker's real cadence. Once the
  Worker exists and its throughput is known, the value should be revisited — 24 hours is a considered
  default, not a measured one.
- Shortening the window increases source load (fewer cache hits, more jobs). That trade-off is now
  explicit and adjustable instead of accidentally pinned to the retention period.
- Reuse of `InProgress` and `Failed` jobs remains `[OPEN]` (`docs/domain.md`). They are **not** reused,
  and that gap is unchanged by this decision.
- The dedupe lookup filters on `(parameters_hash, status, completed_at)` while only `parameters_hash` is
  indexed. Accepted for now: the result set per hash is tiny and the table is empty. A composite index is
  a measurement-driven change, not a precaution.
