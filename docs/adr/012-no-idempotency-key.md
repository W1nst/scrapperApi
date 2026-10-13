# ADR 012 — No `idempotency_key`; dedupe is server-derived

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stated in:** [`docs/domain.md#identity-and-dedupe`](../domain.md#identity-and-dedupe), [`docs/mcp-tools.md`](../mcp-tools.md)

## Context

Two agents (or one agent twice) may issue the same search. The obvious reflex is an
`idempotency_key` in the request, as payment APIs do. That reflex does not survive inspection here:

- **Nothing stores it.** No column, no table. A client-supplied key could therefore never round-trip —
  the second call could not tell "same key, same request" from "same key, different request" without
  persisting the first request anyway.
- The deduplication that actually matters is derived from the request itself, not from a client
  identifier: identical search parameters should reuse an existing result.

`parameters_hash` already provides that derivation: SHA-256 over canonical JSON of `SearchParameters`
— sorted keys, normalized region (in the same form `list_regions` returns), ISO-8601 dates, explicit
currency, no locale-dependent number formatting.

## Decision

There is **no `idempotency_key`** field — not in the `search_tours` input schema, and not as a column.

- Dedupe is **server-derived** from `parameters_hash`.
- Two agents asking with the same parameters within the TTL window reuse the existing `Completed` job;
  the second call returns `reused: true`.
- The canonicalization above is part of the contract, because a different serialization produces a
  different hash for the same intent.

## Consequences

- The API cannot be called with a key that has no effect. Removing the field removes a whole class of
  "I set the key but got a duplicate anyway" confusion.
- Reuse depends entirely on canonicalization being stable. Changing the canonical form invalidates every
  stored hash and causes a wave of duplicate jobs, so it is a breaking change that needs its own ADR.
- `reused: true` in the response is how the caller learns its call was deduplicated; without that flag
  reuse would be invisible and indistinguishable from a fresh job.
- Reuse of an `InProgress` job is `[OPEN]` — currently only `Completed` jobs are reused. That is a
  deliberate gap, not an oversight.
- `parameters_hash` must be indexed, since lookup happens on every `search_tours` call.
