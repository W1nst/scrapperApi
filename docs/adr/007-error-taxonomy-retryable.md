# ADR 007 — The error taxonomy carries `Retryable`

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stated in:** [`docs/domain.md#error-taxonomy`](../domain.md#error-taxonomy), [`docs/conventions.md#error-handling`](../conventions.md#error-handling)

## Context

Scraping fails for many reasons, and they must not be treated alike. A timeout is worth retrying; a
missing selector will fail identically on every attempt and will keep failing until a human edits
`selectors.json`. Treating both the same produces one of two bad systems: every transient blip
permanently fails a job, or a broken selector is retried forever and burns the queue.

Errors also arrive at two different layers. Some are rejected **before** a job exists and never reach
the database; others are persisted against a job.

## Decision

Every failure carries an **`ErrorCode`** paired with a **`Retryable`** flag, from a closed taxonomy.

| Code | `Retryable` | Meaning |
|---|---|---|
| `NETWORK_TIMEOUT` | yes | navigation exceeded the timeout |
| `NETWORK_UNREACHABLE` | yes | DNS/TLS/connection failure |
| `HTTP_429` | yes | source throttling — back off harder |
| `HTTP_5XX` | yes | source-side failure |
| `BROWSER_CRASH` | yes | Playwright/Chromium died |
| `LEASE_EXPIRED` | yes | worker died mid-job |
| `SELECTOR_NOT_FOUND` | no | required selector missing — markup changed |
| `PAGE_STRUCTURE_CHANGED` | no | item container parsed, fields not |
| `ANTI_BOT_DETECTED` | no | CAPTCHA / block page — see [ADR 014](./014-honest-ua-no-evasion.md) |
| `PARAM_INVALID` | n/a | rejected before the job exists — tool-level only, never persisted |
| `JOB_NOT_FOUND` | n/a | unknown or foreign `job_id` — tool-level only, never persisted |
| `INTERNAL` | depends | bug; retry only after a fix |

- The taxonomy is **closed**: a new failure mode needs a new code, added here and in the docs, not an
  ad-hoc string.
- `Retryable = false` means a human must act — typically to edit `selectors.json`.
- Polly is used for transient network errors **only**.
- Tool-level codes (`PARAM_INVALID`, `JOB_NOT_FOUND`) are never persisted; they are returned as
  `ProblemDetails`-style MCP errors.

## Consequences

- A transient error cannot permanently fail a job, and a permanent error cannot be retried indefinitely
  — both properties come from the same field.
- `Retryable` must be persisted alongside the job, so recovery after a restart knows whether to
  re-queue. This makes it part of the schema, not just an exception property.
- The `INTERNAL` row is deliberately not a boolean: it means "retry only after a fix", which is a policy
  decision, so it is resolved by triage rather than by the flag alone.
- Monitoring groups jobs by `ErrorCode`. A spike in a `Retryable = false` code is a markup-change alarm.
- **A transient error must not permanently fail a job** is a hard rule in `AGENTS.md` §3; this ADR is
  where its mechanism is defined.
