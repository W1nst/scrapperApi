# MCP Tools

Contract of our MCP server toward the external agent. Status legend: [agents.md](./agents.md#status-legend).

## Server metadata

| Field | Value |
|---|---|
| Name | `tour-search` |
| Version | `0.0.1` `[PLANNED]` |
| Transport | **Streamable HTTP** `[DECIDED]` — remote server; `stdio` is not applicable |
| Protocol revision | declare the revision actually implemented; verify the latest at implementation time `[OPEN]` |
| `instructions` | short guidance for the agent: call `list_regions` before `search_tours`; poll `get_search_results` using `poll_after_ms`; never invent a region |
| Auth | `[OPEN]` — **blocking**, see [open-questions.md](./open-questions.md) |

## Tools

### `list_regions`

Added deliberately: without it the agent hallucinates region names, and a wrong region produces a `Completed` job with 0 offers — indistinguishable from a genuinely empty result.

Input:
```json
{ "query": "antalya" }            // optional; empty = all
```
Output:
```json
{
  "regions": [
    { "id": "antalya", "label": "Antalya", "offers_count": 128 }
  ],
  "truncated": false
}
```
Rule: the value passed to `search_tours.region` must be an `id` returned by this tool.

### `search_tours`

Creates a job. **Non-blocking** — returns immediately.

Input:
```json
{
  "region": "antalya",
  "adults": 2,
  "children": 1,
  "hotel_stars": 5,
  "start_date": "2026-06-15",
  "nights": 7,
  "price_min": 800,
  "price_max": 1500,
  "currency": "RUB",
  "idempotency_key": "optional-client-string"
}
```

Output:
```json
{
  "job_id": "0f8fad5b-...",
  "status": "Pending",
  "reused": false,
  "parameters_hash": "sha256:...",
  "poll_after_ms": 5000
}
```

- `reused: true` means an existing `Completed` job with the same `parameters_hash` was returned instead of starting a new one — see [domain.md](./domain.md#identity-and-dedupe).
- `poll_after_ms` is the server's hint for the first poll. The agent must not poll tighter than this.

### `get_search_results`

Input:
```json
{
  "job_id": "0f8fad5b-...",
  "cursor": null,          // absent = first page
  "limit": 20              // default 20, max 50
}
```

Output — not ready:
```json
{
  "status": "InProgress",
  "poll_after_ms": 10000,
  "progress": { "offers_so_far": 12, "pages_done": 3, "pages_total": null }
}
```

Output — ready:
```json
{
  "status": "Completed",
  "offers": [
    {
      "hotel_name": "Rixos Premium Belek",
      "hotel_stars": 5,
      "luxury": true,
      "region": "antalya",
      "check_in": "2026-06-15",
      "nights": 7,
      "price": 1240.0,
      "currency": "RUB",
      "room_type": "Standard",
      "meal_type": "AI",
      "source_url": "https://..."
    }
  ],
  "page": { "next_cursor": "eyJv...", "has_more": true, "total_offers": 137 },
  "degraded": false,
  "warnings": [],
  "offers_total": 137,
  "scraped_at": "2026-01-15T09:00:00Z"
}
```

Output — empty but valid:
```json
{ "status": "Completed", "offers": [], "offers_total": 0, "page": { "has_more": false } }
```

Output — failed:
```json
{
  "status": "Failed",
  "error_code": "NETWORK_TIMEOUT",
  "error_message": "Timeout while scraping TourVisor",
  "retryable": true,
  "attempt": 2
}
```

### `cancel_search` (optional)

`Pending` → `Cancelled`; an `InProgress` job gets a cancellation flag and the worker releases it at the next navigation.

```json
{ "job_id": "0f8fad5b-..." }
```

### `list_recent_searches` (optional)

```json
{ "limit": 10 }
```
```json
{ "searches": [ { "job_id": "...", "status": "Completed", "created_at": "...", "parameters_hash": "..." } ] }
```

> `[OPEN]` — leakage risk: jobs are not namespaced per agent, so one agent can see another agent's searches. Either namespace jobs by caller identity (requires auth first) or drop this tool.

## Pagination — mandatory

`get_search_results` must **never** return the whole offer list. A scraping job can produce hundreds of offers; a single unbounded response blows the agent's context window, which is the most expensive failure mode in this system.

- `limit` default 20, max 50.
- Cursor is opaque (`next_cursor`), not an offset.
- Ordering is `(created_at, id)` — never by `Guid` (see [domain.md](./domain.md)).
- The agent is expected to ask for the top-N, not to page through everything. Server-side ranking default: `price` ascending, then `hotel_stars` descending. `[OPEN]` whether the agent should be able to request a different sort.

## Polling vs push `[OPEN]`

Default is polling with `poll_after_ms`. Streamable HTTP can also deliver progress via SSE. The decision is blocking for the implementation; the contract must state one, not both.

## Errors — two different layers

The previous version conflated HTTP `400`/`429` with tool results. They are different layers and must not be mixed:

| Layer | Mechanism | Use |
|---|---|---|
| **Protocol error** | JSON-RPC `error` object | malformed request, unknown tool, bad `job_id` shape, auth failure |
| **Tool-level error** | tool result with `isError: true` + structured content | invalid parameters (`adults < 1`), rate limit hit, `job_id` not found / not yours |
| **Application state** | normal result, `status: "Failed"` | the job ran and failed — this is data, not an error |
| **Transport HTTP status** | `400`/`429`/`5xx` on the Streamable HTTP endpoint | only for the transport, never encoded into a tool payload |

`[FACT]` modern MCP clients rely on `structuredContent` + `outputSchema` for machine-readable results; declare `outputSchema` for every tool, otherwise the agent parses free-form text and guesses.

Unknown or foreign `job_id` → tool-level error with code `JOB_NOT_FOUND` (do not distinguish "does not exist" from "belongs to someone else" — that distinction is an information leak).

## Rate limiting

`search_tours` enforces a per-caller active-job limit. Until auth exists, "per caller" is undefined — see [open-questions.md](./open-questions.md). Until then the limit is global and must be documented as such, not presented as per-agent.
