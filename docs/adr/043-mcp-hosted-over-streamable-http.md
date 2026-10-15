# ADR 043 — The MCP server is hosted over Streamable HTTP in the API process

- **Status:** Accepted
- **Date:** 2026-10-15
- **Stated in:** [`docs/mcp-tools.md#server-metadata`](../mcp-tools.md), [`docs/runtime.md#ports-and-health`](../runtime.md#ports-and-health)
- **Related:** [ADR 001](./001-api-does-not-depend-on-scraper.md), [ADR 004](./004-backgroundservice-started-by-worker-only.md), [ADR 044](./044-tools-ship-as-stubs-first.md)

## Context

`docs/mcp-tools.md` already recorded the transport as `[DECIDED]`: **Streamable HTTP**, because the server
is remote and `stdio` does not apply. What was **not** recorded is *how* — and the project file could not
express it at all.

`[FACT]` verified by reproduction on 2026-10-15. `src/TourSearch.Api.csproj` referenced
`ModelContextProtocol` 2.1.0. That package — and `2.2.0` — contains **no** ASP.NET Core integration: no
`MapMcp`, no `WithHttpTransport`, no `StreamableHttp`, no `IEndpointRouteBuilder`. It exposes only
`WithStdioServerTransport` and `WithStreamServerTransport` (a raw two-way stream). The HTTP hosting lives in
a separate package:

| Package | `MapMcp` | `WithHttpTransport` | `StreamableHttp` |
|---|---|---|---|
| `ModelContextProtocol` 2.1.0 | no | no | no |
| `ModelContextProtocol` 2.2.0 | no | no | no |
| `ModelContextProtocol.AspNetCore` 2.2.0 | **yes** | **yes** | **yes** |

So the comment in `Api.csproj` ("Transport must be Streamable HTTP, not the stdio default") was describing
something the referenced package could not do. A reader following it would add `MapMcp` and get a
compile error with no obvious cause.

A second fact shapes the hosting: `[FACT]` `initialize` with `protocolVersion: "2026-07-28"` is **rejected**
by the SDK with `-32022`, listing the supported revisions as
`2024-11-05, 2025-03-26, 2025-06-18, 2025-11-25`. That is not an SDK deficiency — it is what the revision
means. `docs/mcp-tools.md` states it: the revision "**removes protocol-level sessions**: there is no
`Mcp-Session-Id` header, **no initialize handshake**, and every request is self-contained". A revision that
removed the handshake cannot be negotiated *through* the handshake.

## Decision

The API hosts MCP over **Streamable HTTP**, using **`ModelContextProtocol.AspNetCore` 2.2.0**, mapped at
**`/mcp`**, in **stateless** mode.

- `Api.csproj` references `ModelContextProtocol.AspNetCore` 2.2.0 **alone** — it brings the base package
  transitively, so keeping the old bare reference would be a version conflict, not redundancy.
- Endpoint path: `/mcp`, documented in `docs/runtime.md` and `docs/mcp-tools.md`.
- Transport: `WithHttpTransport(http => http.Stateless = true)`. There is no session state to keep alive
  and the current revision removes protocol-level sessions, so stateless matches the contract.
- The server accepts **both** client styles:
  - a legacy handshake client, `initialize` with a revision that supports the handshake (e.g.
    `2025-11-25`) — returns a normal result naming `tour-search`;
  - a per-request-metadata client at `2026-07-28`, which receives `-32022` from `initialize` **by design**
    and uses `tools/list` / `tools/call` with no handshake at all.
- `/health` reports `protocol_revision` from configuration (`Mcp:AdvertisedProtocolRevision`, default
  `2026-07-28`) as **server metadata only**. `[FACT]` the SDK exposes no public source for the negotiated
  revision: `McpServerOptions` has no `ProtocolVersion` property and the supported-revision list is
  `internal`. The advertised value is therefore never sent as a protocol claim, never assigned to
  `McpServerOptions`, and negotiation happens per request. `/health` also reports
  `protocol_negotiation: "per-request"` so the distinction is visible to an operator.

`[FACT]` the SDK registers its own `IdleTrackingBackgroundService` as an `IHostedService`, even with
`Stateless = true`, with no option to disable it. ADR 004 governs **our** hosted service — the job loop,
which lives in the Worker. This is SDK transport infrastructure inside the API, and the rule is unaffected;
a test asserts that no **TourSearch-owned** `IHostedService` is registered.

## Consequences

- The documented transport is now the one the code can actually serve, and the constant
  `ModelContextProtocol.AspNetCore` reference is recorded so nobody re-pins the bare package and loses
  `MapMcp` again.
- Stateless hosting means no session store, no sticky routing and no `Mcp-Session-Id` — the API can scale
  horizontally without affinity, and a client reconnect does not need a re-initialize.
- A client that insists on `initialize` with `2026-07-28` gets `-32022`. That is correct behaviour and
  must **not** be "fixed" by forcing an older revision or by hiding the error: the client is using a
  revision that has no handshake.
- `/health`'s `protocol_revision` is operator metadata, not a negotiated fact. Anything that needs the
  real negotiated revision must read it from the request, not from config. Documented so nobody treats the
  field as authoritative.
- The API process gains a hosted service it does not own, which is visible in the DI graph. Recorded here
  so a future reader does not mistake it for a violation of ADR 004.
- Requires `Microsoft.NET.Sdk.Web` and an HTTP endpoint, so the API can no longer be a slim console host.
  Already the case.
