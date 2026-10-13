# ADR 005 — Job claim is an atomic compare-and-set with a lease and a recovery pass

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stated in:** [`docs/database.md#job-claim-mechanism`](../database.md#job-claim-mechanism), [`docs/runtime.md#restart-semantics`](../runtime.md#restart-semantics), [`docs/domain.md`](../domain.md)

## Context

A worker may be killed at any moment (deploy, OOM, `SIGTERM` past the grace period, machine loss). The
queue must be recoverable without human intervention, and two workers must never take the same job.

A read-then-write claim cannot provide that: EF Core has no atomic CAS across a read and an update.
`ExecuteUpdate` writes a row; it does not make "read `Pending` → write `InProgress`" atomic across two
statements. Two workers doing that concurrently both observe `Pending` and both proceed.

Without a recovery pass, a crash leaves a job `InProgress` forever, and no TTL rule rescues it, because
the job is not old — it is merely stuck.

## Decision

The claim is an **atomic compare-and-set**, not a read-then-write.

- **Atomic transition:** `Pending → InProgress`, writing `LeaseToken`, `LeaseExpiresAt` and
  `Attempt + 1` in the **same statement**.
- **Mechanism:** hand-written Npgsql inside the Infrastructure layer — one statement,
  `UPDATE search_jobs WHERE status = 'Pending' … ORDER BY created_at LIMIT 1 … RETURNING …`, expressed
  with `db.Query<SearchJob>().UpdateFrom<SearchJob>(...)` / `SqlExpression`, wrapped in
  `db.Transaction(...)` where a second write is part of the claim.
- **Lease:** `LeaseToken` is the claiming worker's id; `LeaseExpiresAt` bounds how long the claim is
  valid.
- **Recovery pass on startup:** `InProgress` with `LeaseExpiresAt < now` → back to `Pending`,
  `Attempt + 1`, `ErrorCode = LEASE_EXPIRED`, `Retryable = true`.
- **Index:** the recovery query needs its own index — `(status, lease_expires_at)`. Without it every
  startup scans the whole table.
- **Cancellation:** cancellation means "stop and **release the lease**", never "finish the job anyway".

## Consequences

- A killed worker is recoverable by construction: the lease expires, the recovery pass returns the job
  to `Pending`, and `Attempt` records that it happened.
- Correctness depends on the statement being a single atomic operation. Any refactor into
  read-then-write silently reintroduces double-pickup — this is a system bug, not a style issue.
- `Attempt` is the only counter that survives a crash, so retry limits must be expressed against it
  rather than against in-memory state.
- The `(status, lease_expires_at)` index is mandatory, not an optimisation. Dropping it turns every
  worker startup into a full-table scan.
- Tests must cover: concurrent claim (only one winner), expired-lease recovery, and cancellation
  releasing the lease. `LEASE_EXPIRED` must be `Retryable = true` so a crash does not permanently fail
  a job.
- The migration that creates this schema must be generated against the single runtime `DbContext`
  ([ADR 002](./002-migration-references-runtime-dbcontext.md)).
