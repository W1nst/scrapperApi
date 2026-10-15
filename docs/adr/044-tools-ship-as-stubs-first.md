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
