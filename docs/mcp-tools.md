# MCP Tools

Contract of our MCP server toward the external agent. Status legend: [agents.md](./agents.md#status-legend).

## Server metadata

| Field | Value |
|---|---|
| Name | `tour-search` |
| Version | `0.0.1` |
| Endpoint | `/mcp` `[FACT]` — Streamable HTTP, stateless (`WithHttpTransport`). `Api` references `ModelContextProtocol.AspNetCore` 2.2.0; the bare `ModelContextProtocol` package has **no** `MapMcp` — see [ADR 043](./adr/043-mcp-hosted-over-streamable-http.md) |
| Transport | **Streamable HTTP** `[DECIDED]` — remote server; `stdio` is not applicable |
| Protocol revision | `2026-07-28` `[FACT]` — the latest spec revision as of 2026-10; re-verify at implementation time. It **removes protocol-level sessions**: there is no `Mcp-Session-Id` header, no initialize handshake, and every request is self-contained (per-request protocol version + client capabilities). Consequences for our server: no session state to keep alive, `tools/list` is cacheable via `ttlMs`, and `server/discover` replaces the old handshake. Sources: <https://github.com/modelcontextprotocol/modelcontextprotocol> and <https://modelcontextprotocol.io/specification/2026-07-28> |
| **Handshake behaviour** | `[FACT]` verified by reproduction (2026-10-15, SDK 2.2.0): a revision that removed the handshake cannot be negotiated **through** the handshake. `initialize` with `2026-07-28` is rejected with `-32022` and lists the supported revisions — `2024-11-05`, `2025-03-26`, `2025-06-18`, `2025-11-25`. That is correct, not a defect. Our server therefore accepts **both** styles: a legacy client initializes with `2025-11-25` (or another handshake revision) and gets a normal result; a `2026-07-28` client skips `initialize` entirely and calls `tools/list` / `tools/call` with per-request metadata. Never "fix" the `-32022` by pinning an older revision or hiding the error |
| `instructions` | short guidance for the agent: call `list_regions` before `search_tours`; poll `get_search_results` using `poll_after_ms`; never invent a region |
| Auth | `[OPEN]` — **blocking**, see [open-questions.md](./open-questions.md) |

## Tool set

`[DECIDED]` — the shipped set is **exactly three** tools: `list_regions`, `search_tours`, `get_search_results`. `cancel_search` is specified but optional (it needs `CancelRequestedAt` in the domain model — see [domain.md](./domain.md)). `list_recent_searches` is **blocked**, not optional: without caller identity it leaks other agents' searches, so it ships only after auth is decided.

### `list_regions`

Added deliberately: without it the agent hallucinates region names, and a wrong region produces a `Completed` job with 0 offers — indistinguishable from a genuinely empty result.

`[DECIDED]` **Implemented** (task 005). It reads the `regions` catalog from the database and computes
`offers_count` from `tour_offers` in one grouped query (not one query per region). Only `Active = true`
regions are returned; results are ordered `label` ascending with `id` as tiebreak, so repeated calls are
stable. The catalog is seeded by migration, never fetched at runtime
([ADR 046](./adr/046-region-catalog-seeded-by-migration.md)), so this tool makes no external call.
Current catalog: **85 Турция regions** (20 parents, 65 children) — the response is flat, and a child such
as `kemer-tsentr` is a valid `search_tours.region` value in its own right.

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
Rule: the value passed to `search_tours.region` must be an `id` returned by this tool. **Never the
`label`** — labels are display text and are not accepted; and never a source's numeric id, which is the
parser's business ([ADR 045](./adr/045-region-identity-slug-plus-source-id.md)).

`offers_count` is `0` for a region with no offers yet, and for every region while `tour_offers` is empty.
`0` is a real count, not a signal that the catalog is missing — the catalog is present whenever regions
are returned at all.

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
  "currency": "RUB"
}
```

`[DECIDED]` — there is **no** `idempotency_key` field: nothing stores it, so a client-supplied key could never round-trip. Dedupe is server-derived from `parameters_hash` (see [domain.md](./domain.md#identity-and-dedupe)).

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

Output — not ready (`status` is `Pending` or `InProgress`; same shape for both):
```json
{
  "status": "InProgress",
  "poll_after_ms": 10000,
  "progress": { "offers_so_far": 12, "pages_done": 3, "pages_total": null }
}
```

`progress` maps to the domain fields `OffersCount` (running), `PagesDone`, `PagesTotal` — see [domain.md](./domain.md). `pages_total` is `null` when the source reports no total.

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
  "page": { "next_cursor": "eyJv...", "has_more": true },
  "degraded": false,
  "warnings": [],
  "offers_total": 137,
  "scraped_at": "2026-01-15T09:00:00Z"
}
```

`offers_total` is the single authoritative count (= `OffersCount`); there is no second count inside `page` — one number, one name.

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

`Pending` → `Cancelled` directly. An `InProgress` job gets `CancelRequestedAt` set and the Worker releases the lease at the next navigation boundary — see [domain.md](./domain.md) and [runtime.md](./runtime.md#restart-semantics).

```json
{ "job_id": "0f8fad5b-..." }
```

Output: `{ "status": "Cancelled" }` or a tool-level `JOB_NOT_FOUND`.

### `list_recent_searches` (**blocked**)

```json
{ "limit": 10 }
```
```json
{ "searches": [ { "job_id": "...", "status": "Completed", "created_at": "...", "parameters_hash": "..." } ] }
```

> `[OPEN]` — **blocking**, not optional: jobs are not namespaced per agent, so one agent can see another agent's searches. Either namespace jobs by caller identity (requires auth first) or drop the tool. Until then it is not shipped.

## Pagination — mandatory

`get_search_results` must **never** return the whole offer list. A scraping job can produce hundreds of offers; a single unbounded response blows the agent's context window, which is the most expensive failure mode in this system.

- `limit` default 20, max 50.
- Cursor is opaque (`next_cursor`), not an offset.
- **Ordering must match the sort the page is served in.** Offers are returned in server-rank order: `price` ascending, then `hotel_stars` descending, then `id` as a stable tiebreaker — so the cursor encodes `(price, hotel_stars, id)`. `[DECIDED]`: the `(created_at, id)` cursor in [domain.md](./domain.md#guid-ordering) is for **job** listing (`list_recent_searches`); it is not the offer cursor, and `TourOffer` has no `CreatedAt` at all — its timestamp is `ScrapedAt`. Mixing the two cursors is a contract bug.
- A rank cursor stays stable while a job is still scraping (new offers are inserted, not appended at an offset); offset pagination over a growing result set is not.
- The agent is expected to ask for the top-N, not to page through everything. `[OPEN]` whether the agent should be able to request a different sort — if it can, the cursor must encode the sort it was issued for.

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

`[FACT]` modern MCP clients rely on `structuredContent` + `outputSchema` for machine-readable results; declare `outputSchema` for every tool, otherwise the agent parses free-form text and guesses. `[FACT]` in the JSON-RPC error object, method-not-found / invalid-params is `-32602` (the older `-32002` transport-level code was removed); MCP-specific codes are `-32020` HeaderMismatch, `-32021` MissingRequiredClientCapability, `-32022` UnsupportedProtocolVersion — see <https://modelcontextprotocol.io/specification/2026-07-28/basic>

Unknown or foreign `job_id` → tool-level error with code `JOB_NOT_FOUND` (do not distinguish "does not exist" from "belongs to someone else" — that distinction is an information leak). `JOB_NOT_FOUND` and `PARAM_INVALID` are the only two codes that never reach the DB — see [domain.md](./domain.md#error-taxonomy).

## Rate limiting

`search_tours` enforces an active-job limit. Until auth exists, "per caller" is undefined — see [open-questions.md](./open-questions.md). Until then the limit is **global** and must be documented as such, not presented as per-agent.

`[DECIDED]` — the limit is computed, not stored: `COUNT(search_jobs WHERE status IN ('Pending','InProgress'))` served by the `(status, created_at)` index (see [database.md](./database.md#index-rules)). A stored counter would need the same CAS discipline as the job claim and would drift on every crash.
