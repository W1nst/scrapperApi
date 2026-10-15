# ADR 048 — `RATE_LIMITED` is a tool-level code, and the limit is global and computed

- **Status:** Accepted
- **Date:** 2026-10-15
- **Stated in:** [`docs/domain.md#error-taxonomy`](../domain.md#error-taxonomy), [`docs/mcp-tools.md#rate-limiting`](../mcp-tools.md#rate-limiting)
- **Related:** [ADR 007](./007-error-taxonomy-retryable.md), [ADR 005](./005-job-claim-atomic-cas.md), [ADR 038](./038-parameter-validation-at-tool-layer.md)

## Context

`search_tours` enforces an active-job limit, and reaching it must be reported somehow. The taxonomy in
`docs/domain.md#error-taxonomy` had **12 codes and none of them fit**: `INTERNAL` means "a bug, retry after
a fix", which a deliberate refusal is not, and inventing an ad-hoc string would break the taxonomy's
"closed set" property.

Two further questions had to be settled together:

- **Where does the refusal live?** `docs/mcp-tools.md#errors--two-different-layers` splits protocol errors
  (JSON-RPC `error`, e.g. `-32602`) from tool-level errors (a result with `isError: true`). A rate limit is
  a property of a **well-formed request**, so it cannot be a protocol error: `-32602` means "your request
  was malformed", and returning it here would tell an agent to fix JSON that is already valid.
- **Stored counter or computed?** A counter needs the same CAS discipline as the job claim (ADR 005) and
  would drift on every crash — a crash mid-job leaves the counter incremented for work that no longer
  runs, permanently shrinking the capacity until someone resets it.

## Decision

**`RATE_LIMITED`** joins the taxonomy as a **tool-level, never-persisted** code — the third such code,
beside `PARAM_INVALID` and `JOB_NOT_FOUND`. It is returned as a result with `isError: true` and
`structuredContent: { error_code, message }` — **no `field`**, because no parameter is at fault; naming one
would send the agent to "fix" a request that is already correct.

The limit itself:

- **Global**, not per-caller, and described as global. `docs/mcp-tools.md#rate-limiting` forbids presenting
  it as per-agent while caller identity does not exist (open question 2).
- **Computed** on every call: `COUNT(search_jobs WHERE status IN ('Pending','InProgress'))`, served by the
  existing `(status, created_at)` index. Never stored, so no counter can drift.
- Default **10**, configurable.
- **Reuse is checked before the limit.** A reused job creates no work, so it must never be refused by a cap
  it does not consume; otherwise dedupe fails precisely under load, which is when it matters most.
- At the limit, **no row is created** — the request is refused, not queued.

**Accepted non-atomicity.** Two concurrent requests can both observe `count = limit - 1` and both insert,
yielding `limit + 1` active jobs. This is deliberate: the limit is a **coarse load-shedding guard**, not a
correctness invariant, and making it exact would require serialising every `search_tours` call — an
unacceptable cost for a self-inflicted, self-correcting overshoot. The alternative (a stored counter) was
rejected above for the same reason a race is tolerable: the cost of being slightly wrong is far lower than
the cost of being exactly wrong after a crash.

## Consequences

- An agent gets a **correctable** answer: a distinct code it can back off on, not a generic failure and not
  a malformed-request error.
- The limit can be raised or lowered without a migration, and it degrades safely: a crash cannot
  permanently consume capacity, because nothing is remembered between calls.
- **The overshoot is real and documented.** Anyone reading the limit as a hard cap is wrong; it is a guard
  with slack up to the number of concurrent requests.
- `RATE_LIMITED` must **never** be persisted on a job. A refused request leaves no trace beyond a log line,
  exactly like `PARAM_INVALID`.
- When auth arrives, the limit becomes per-caller and this code's meaning narrows to "this caller is at its
  own cap". That is a documented change of behaviour, and the tool description must be updated with it —
  it currently states the limit is global, which will become false.
