# ADR 004 — `BackgroundService` is started by the Worker only

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stated in:** [`docs/runtime.md#startup`](../runtime.md#startup), [`docs/conventions.md#di`](../conventions.md#di)

## Context

`Api` and `Worker` share `Core` and `Infrastructure`, and both build a DI graph. If both registered and
resolved a `BackgroundService`, the same job would be picked up twice — the exact failure the atomic
claim CAS (see [ADR 005](./005-job-claim-atomic-cas.md)) exists to prevent, arriving through
configuration instead of through code.

## Decision

Exactly **one** process starts a `BackgroundService`: the **Worker**.

- **Api** — DI graph is `AddInfrastructure()` only. It never registers or resolves a `BackgroundService`.
- **Worker** — DI graph is `AddInfrastructure()` + `AddScraper()`; the `BackgroundService` is registered
  as a service that is resolved at startup, so its loop begins with the process and holds the process
  `CancellationToken`.

| Process | Type | Starts `BackgroundService` | Uses Playwright |
|---|---|---|---|
| `TourSearch.Api` | ASP.NET Core (Minimal APIs) | **no** | **no** |
| `TourSearch.Worker` | console executable | **yes — the only one** | yes |

Startup must fail fast on bad configuration (missing DB credentials, unknown region set) — not half-start
and then loop doing nothing. Exit codes are `0` clean, `1` bad configuration, `2` unrecoverable internal
error.

## Consequences

- Job pickup is single-owner by construction. Scaling means more **Worker processes**, not more
  `BackgroundService` instances inside one process.
- Horizontal scale remains safe because the claim is a CAS: several workers contend, and exactly one
  wins a given job.
- The API graph is smaller and has no hosted-service lifetime to manage; it can be deployed without any
  scraping concern.
- Any future component that needs a background loop in the API process must be an explicit, separately
  justified design — not a second `AddHostedService` call copied from the Worker.
- Enforced by convention plus review: the rule is recorded in `docs/runtime.md`, in
  `docs/conventions.md#di`, in this ADR, and as a comment in `TourSearch.Api.csproj`.
