# ADR 010 — `list_regions` exists so the agent cannot invent a region

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stated in:** [`docs/mcp-tools.md`](../mcp-tools.md), [`docs/agents.md`](../agents.md)

## Context

The first tool an agent needs is the set of valid regions. Without it the agent hallucinates region
names, and the failure is nearly undetectable: a wrong region produces a `Completed` job with **0
offers**, which is indistinguishable from a genuinely empty result. The agent then reports "no tours
available" for a region that was never searched.

A hardcoded list inside the prompt or the tool description is not a fix either — it drifts from the
catalog and cannot be kept in one place.

## Decision

`list_regions` is part of the shipped tool set, and it is the **only** source of valid region ids.

- Input: `{ "query": "antalya" }` — optional; empty means all regions.
- Output: `{ "regions": [ { "id", "label", "offers_count" } ], "truncated": false }`.
- **Rule:** the value passed to `search_tours.region` must be an `id` returned by this tool.
- Server `instructions` must tell the agent: call `list_regions` before `search_tours`, and never invent
  a region.

## Consequences

- The "wrong region → empty result" failure mode is designed out rather than documented away. A bad
  region id can now be rejected, because there is an authoritative set to validate against.
- The agent needs a round trip before its first search. That cost is accepted deliberately — it is far
  cheaper than a confident wrong answer.
- `offers_count` is **computed** from `tour_offers` through the `region` index, not stored. A stored
  counter would drift; the computed count cannot.
- `truncated` must be reported honestly, so the agent knows the list was cut and can narrow its query
  instead of assuming the region does not exist.
- This tool is one of the **three** shipped tools. `cancel_search` is optional and `list_recent_searches`
  is blocked until caller identity exists — see `docs/mcp-tools.md`.
