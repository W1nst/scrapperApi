# ADR 044 — The three MCP tools ship as honest stubs before their behaviour exists

- **Status:** Accepted
- **Date:** 2026-10-15
- **Stated in:** [`docs/mcp-tools.md#tool-set`](../mcp-tools.md)
- **Related:** [ADR 010](./010-list-regions-prevents-invented-regions.md), [ADR 012](./012-no-idempotency-key.md), [ADR 043](./043-mcp-hosted-over-streamable-http.md)

## Context

The contract in `docs/mcp-tools.md` is normative and detailed: three tools, exact names, input and output
field names, `outputSchema` required, pagination mandatory, error semantics split across two layers. None
of the behaviour exists yet — there is no region catalog, no job creation, no scraping.

The tempting shortcut is to wire `list_regions` to the empty `regions` table, so "a tool works". That
produces the **exact** failure `list_regions` was added to prevent (ADR 010): an empty catalog is
indistinguishable from "this region does not exist", and an agent that asks for tours in a region and gets
zero offers cannot tell a missing catalog from a genuinely empty result. A tool that returns `[]` truthfully
while lying by implication is worse than a tool that says it is not ready.

## Decision

All three tools — `list_regions`, `search_tours`, `get_search_results` — ship as **honest stubs** in this
milestone.

- Each returns a **normal** result (`isError: false`) whose text states that it is not implemented and
  **names the missing prerequisite** (e.g. "the region catalog is not seeded yet").
- Each declares `OutputSchemaType` and `UseStructuredContent`, so **`outputSchema` is present in
  `tools/list`** exactly as the contract requires — the surface is complete even where the behaviour is not.
- No stub returns `structuredContent`. `[FACT]` the SDK permits a tool to declare an output schema and still
  return text only, so an honest stub never has to fabricate a conforming payload. This is what makes the
  decision implementable rather than merely principled.
- `search_tours` creates **no** job. `get_search_results` queries **no** database. `list_regions` reads **no**
  catalog.
- Each tool **description** ends with a "NOT IMPLEMENTED in this build" sentence, so a caller reading
  `tools/list` alone is warned — not only a caller who invokes the tool. These sentences are removed when
  the tools are implemented.
- Tools that are **blocked**, not merely unimplemented, are not registered at all: `list_recent_searches`
  (needs caller identity) and `cancel_search` (needs cancellation ownership). A stub for those would imply
  they are coming sooner than they are.

## Consequences

- The MCP contract becomes **testable now**: a client connects, lists three tools, and the schemas can be
  compared field for field against `docs/mcp-tools.md`. Contract drift fails a test instead of being
  discovered by an agent in production.
- An agent cannot be misled. It is told, in the tool description *and* in the response, that no data
  exists — which is the only honest option until the catalog and the job queue are real.
- The stub text is deliberately duplicated (description and result). That redundancy is the point and is
  temporary; it is removed with the implementation.
- **A stub must never be mistaken for a partial implementation.** If a future change wires one tool to real
  data, the others stay stubs and the discriminator becomes ambiguous. Implement all three together, or
  state clearly in the contract which are live.
- This raises the cost of the first real tool, because it must also remove the "not implemented" text and
  update the tests that assert it.
- `list_regions` remains the highest-value first implementation, because every other tool depends on a
  valid region id (ADR 010), and it is blocked on the region catalog rather than on the job queue.

## Update 2026-10-15 — `search_tours` is now live too

Task `006` implemented `search_tours` for real (validation, canonical hash, dedupe, global limit, `Pending`
job). **`get_search_results` is now the only stub**, and the discriminator set is again enforced by a test:
only `get_search_results` carries the "NOT IMPLEMENTED in this build" suffix and stub text.

The no-worker caveat is handled rather than hidden: because nothing executes a job, `search_tours`'
description and the server `instructions` state plainly that **no worker exists in this build**, that a
created job stays `Pending`, and that it will never produce offers. An agent is told not to wait for a
result that cannot arrive. That text is removed when the Worker ships.

## Update 2026-10-15 — `list_regions` is now live

The prediction above came true earlier than the rest: task `005` implemented `list_regions` for real
(region catalog, computed `offers_count`), while `search_tours` and `get_search_results` remain stubs.

How the ambiguity is handled — the discriminator is now **per tool**, and it is enforced by a test rather
than by prose:

- `list_regions`'s description no longer carries the "NOT IMPLEMENTED in this build" suffix, and its
  result is real data.
- `search_tours` and `get_search_results` **keep** both the description suffix and the stub result text.
- A test asserts the exact set: only `search_tours` and `get_search_results` still say NOT IMPLEMENTED. A
  third tool silently becoming live, or a stub losing its marker, fails the build.

So "the discriminator becomes ambiguous" is answered by making the marker explicit per tool and asserting
it. The remaining two tools should be implemented together where possible — `search_tours` creates the job
that `get_search_results` reads, so a lone implementation of either is not useful to an agent.
