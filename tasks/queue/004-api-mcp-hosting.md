# 004 — The API process and the MCP server hosting

| | |
|---|---|
| **Status** | `ready` |
| **Type** | feature |
| **Created (UTC)** | `2026-10-15` |
| **Affected projects** | `TourSearch.Api`, `tests/TourSearch.UnitTests` |
| **Source request** | > «Давай зайдем с другой стороны, с апи/mcp» → уточнено до варианта A: каркас API и MCP-хостинг |

## Summary

Replace the `TourSearch.Api` placeholder with a real, running MCP server over **Streamable HTTP**:
process startup, `/health`, the MCP endpoint, and the **three** tools from the contract registered with
their exact names, descriptions, input schemas and `outputSchema` — each returning an honest
`not implemented` result rather than fake data.

This is `AGENTS.md` §2 step 8, deliberately **without** tool business logic. The goal is that an MCP
client can connect to the server and see the contract exactly as `docs/mcp-tools.md` specifies, while no
tool pretends to work.

## Problem

`TourSearch.Api/Program.cs` is a placeholder that writes to stderr and exits `1`. Nothing in the
specification is reachable: no server starts, no endpoint answers, and the MCP contract exists only as
prose.

Three facts were established by reproduction while preparing this task, and the first one is a **defect in
the current project file**:

1. **The MCP reference in `TourSearch.Api.csproj` cannot deliver the specified transport.**
   `[FACT]` The project references `ModelContextProtocol` **2.1.0**. That package — and also `2.2.0` —
   contains **no** ASP.NET Core integration: no `MapMcp`, no `WithHttpTransport`, no `StreamableHttp`, no
   `IEndpointRouteBuilder`. It offers only `WithStdioServerTransport` and `WithStreamServerTransport` (a
   raw two-way stream). The HTTP hosting lives in a **separate package**,
   `ModelContextProtocol.AspNetCore`. Verified by inspecting the assemblies of both versions, and by
   building a probe with and without the AspNetCore package:

   | Package | `MapMcp` | `WithHttpTransport` | `StreamableHttp` |
   |---|---|---|---|
   | `ModelContextProtocol` 2.1.0 | no | no | no |
   | `ModelContextProtocol` 2.2.0 | no | no | no |
   | `ModelContextProtocol.AspNetCore` 2.2.0 | **yes** | **yes** | **yes** |

   So the comment in `Api.csproj` ("Transport must be Streamable HTTP, not the stdio default") is
   currently unreachable — the referenced package cannot express it.

2. **`2.2.0` is available; the pin is one minor behind.** `[FACT]` NuGet lists `2.2.0` as the latest
   stable of all three packages.

3. **The docs' protocol revision is real.** `[FACT]` the SDK 2.2.0 assemblies contain the strings
   `2025-11-25` **and** `2026-07-28`, and <https://modelcontextprotocol.io/specification/2026-07-28>
   returns HTTP 200. `docs/mcp-tools.md`'s revision is not a placeholder.

A fourth, smaller finding affects the contract's exactness: **the SDK emits `outputSchema` property names
in camelCase by default.** A probe with a `record RegionOut(string Id, string Label, int OffersCount)`
produced `offersCount`, while `docs/mcp-tools.md` specifies `offers_count`. Verified fix: annotate the
record with `[property: JsonPropertyName("offers_count")]` — the schema then emits
`{"id","label","offers_count"}` exactly.

## Scope

### In scope

- `TourSearch.Api.csproj`: reference `ModelContextProtocol.AspNetCore` **2.2.0** (replacing the bare
  `ModelContextProtocol` 2.1.0 reference, which is redundant because AspNetCore brings it transitively —
  verified by a probe that builds with only the AspNetCore package).
- `Program.cs`: real startup — configuration validation, `AddInfrastructure()`, MCP server registration,
  the MCP endpoint, and `/health`.
- The **three** tools registered with the contract's exact names, descriptions, input schemas and
  `outputSchema`, each returning a stub result.
- Exit codes `0` / `1` / `2` per `docs/runtime.md#shutdown`.
- Tests that assert the exposed tool surface against `docs/mcp-tools.md`.

### Out of scope

- **Tool business logic.** Every tool returns `not implemented`. `list_regions` is *not* wired to the
  database: the region catalog is not seeded, and returning an empty list would look like a working tool
  that says "no regions exist" — the exact confusion ADR 010 exists to prevent.
- **Auth.** `docs/mcp-tools.md` marks it `[OPEN]` **blocking**; the user deferred it to its own task. No
  API key, no caller identity, no per-caller rate limit.
- **`list_recent_searches`** — blocked on auth, must not be registered.
- **`cancel_search`** — optional and unowned (`CancelRequestedAt` logic is the Worker's); not registered.
- **The Worker process** (`AGENTS.md` §2 step 7) — untouched.
- **Region seeding** — needs `selectors.json` (step 9).
- **SSE progress / push** — `open-questions.md` #1 is unresolved; the contract's `poll_after_ms` polling
  path is what the stubs describe.
- Deployment, Docker, HTTPS termination — blocking question 4, deferred.
- Frontend, booking, payment, tourist PII, anti-bot evasion — `AGENTS.md` §8.

## Requirements

### Functional

**FR-001 — the API process starts and reports health**

- `dotnet run --project src/TourSearch.Api` starts a server bound to a configured URL.
- `GET /health` returns `200` with server metadata: name `tour-search`, the version, and the protocol
  revision (`docs/runtime.md#ports-and-health`).
- Startup **fails fast** on bad configuration, naming the missing piece
  (`docs/runtime.md#startup`) — no half-start that then loops.
- Exit codes follow `docs/runtime.md#shutdown`: `0` clean, `1` bad configuration, `2` unrecoverable.

*Acceptance:* the process starts; `curl /health` returns 200 with those fields; with a required
`TOURSEARCH_DB_*` variable unset the process exits `1` with the variable named, rather than starting.

**FR-002 — the MCP endpoint serves Streamable HTTP**

- The MCP endpoint is mapped at a documented path (`/mcp`) using `MapMcp`.
- It uses the **HTTP** transport, not stdio — `WithHttpTransport`.
- The transport is **stateless** where the protocol revision permits it, so no session must be kept alive
  (`docs/mcp-tools.md` protocol note).
- `POST /mcp` with a JSON-RPC `initialize` request returns a valid `initialize` result naming
  `tour-search`.
- `POST /mcp` with `tools/list` returns the three tools.

*Acceptance:* the raw HTTP calls above are executed and their real output quoted. A probe proved this
works; the task requires it again in this repository.

**FR-003 — the tool surface matches `docs/mcp-tools.md` exactly**

Registered tools, with **exact** names:

| Tool | Input | Output schema fields |
|---|---|---|
| `list_regions` | `query` (optional string) | `regions[]` (`id`, `label`, `offers_count`), `truncated` |
| `search_tours` | `region`, `adults`, `children`, `hotel_stars`, `start_date`, `nights`, `price_min`, `price_max`, `currency` | `job_id`, `status`, `reused`, `parameters_hash`, `poll_after_ms` |
| `get_search_results` | `job_id`, `limit`, `cursor` | job status fields, `offers[]`, `page.next_cursor`, `offers_total`, `poll_after_ms` |

- Field names in `outputSchema` are **snake_case**, matching the contract — the SDK's camelCase default is
  overridden with `[property: JsonPropertyName(...)]` (verified to work).
- Every tool declares `OutputSchemaType` and `UseStructuredContent`, so `outputSchema` appears in
  `tools/list` (`docs/mcp-tools.md#errors--two-different-layers` calls a missing `outputSchema` a
  correctness problem, not a nicety).
- The `search_tours` tool description states that it is **non-blocking** and that the caller must poll
  `get_search_results` using `poll_after_ms`.
- The `list_regions` description carries the rule: the value passed to `search_tours.region` must be an
  `id` returned by this tool.
- The server's `instructions` carry the contract's guidance: call `list_regions` first; poll
  `get_search_results`; never invent a region.
- **Exactly three** tools are exposed. A test asserts the set, so an accidental fourth (or a missing one)
  fails.

*Acceptance:* `tools/list` output is compared field for field against `docs/mcp-tools.md` and the diff is
empty; a test asserts the tool-name set.

**FR-004 — every tool is an honest stub**

- Each tool returns a result stating it is not implemented, plus which prerequisite is missing — e.g.
  `list_regions` → "the region catalog is not seeded yet".
- No tool returns fabricated data, an empty success, or a plausible-looking default.
- `search_tours` does **not** create a job and `get_search_results` does **not** query the database.
- The MCP layer distinguishes tool-level errors from protocol errors as
  `docs/mcp-tools.md#errors--two-different-layers` requires; a stub is a **normal result**, not an error.

*Acceptance:* each tool is called and its real output quoted; the output contains no invented data.

**FR-005 — the API does not start a `BackgroundService` and does not depend on `Scraper`**

- `AddInfrastructure()` is called; no hosted service is registered (`AGENTS.md` §3, ADR 004).
- `TourSearch.Api` references `Scraper` nowhere, directly or transitively — no Playwright, no Chromium in
  the API process (ADR 001).
- Nothing applies migrations at startup (ADR 041).

*Acceptance:* a test asserts the service graph contains no `IHostedService` from TourSearch; a grep shows
no `Scraper` reference in the API's dependency closure.

**FR-006 — configuration is documented and secrets stay out**

- The HTTP URL and MCP path are configurable, with documented defaults; the environment variable names
  are consistent with `docs/runtime.md`.
- **No secret appears in any committed file**, including `appsettings.json` and examples (ADR 003).
- `appsettings.json` carries host/port/database **defaults only**, matching
  `docs/architecture.md#configuration-and-secrets`.

*Acceptance:* grep over the diff for password-like literals is empty; the code reads the documented names.

### Non-functional

**NFR-001 — the contract is verifiable without a browser**

The tool surface is asserted by tests that inspect the registered tools and their schemas, so a contract
drift fails the build rather than being discovered by an agent at runtime. The
`npx @modelcontextprotocol/inspector` check from `AGENTS.md` §4 is run manually for the transport, and
**never against the live site**.

**NFR-002 — no fabricated readiness**

A stub must never be mistakable for a working tool, in the payload or in the docs. If any tool is wired to
real data during implementation, that over-delivers and must be stopped rather than silently accepted —
this task's value is a trustworthy contract surface, not partial behaviour.

**NFR-003 — the process is a good citizen**

No port is left bound after tests; no external service is contacted; no write to the database. The MCP
endpoint does not require a database connection to answer `tools/list` (the stub tools touch no DB).

## Technical design

### Affected projects and dependency direction

| Project | Change | Does it break `Api` ↛ `Scraper`? |
|---|---|---|
| `TourSearch.Api` | real `Program.cs`; `ModelContextProtocol.AspNetCore` 2.2.0; `appsettings.json` defaults | no — no new project reference at all |
| `tests/TourSearch.UnitTests` | tool-surface and graph tests | no |

`Api` keeps referencing only `Core` + `Infrastructure`.

### Package change (verified)

```
- <PackageReference Include="ModelContextProtocol" Version="2.1.0" />
+ <PackageReference Include="ModelContextProtocol.AspNetCore" Version="2.2.0" />
```

`[FACT]` the AspNetCore package brings the base package transitively: a probe referencing **only**
`ModelContextProtocol.AspNetCore` 2.2.0 compiled `AddMcpServer`, `WithHttpTransport`, `WithTools` and
`MapMcp` successfully with `TreatWarningsAsErrors` on. Keeping the old bare reference at 2.1.0 alongside
would be a version conflict, not a belt-and-braces.

### Verified probe (reproduction, 2026-10-15)

A throwaway `net10.0` web project with `TreatWarningsAsErrors` produced a working server:

```
GET  /health                 -> {"name":"tour-search","version":"0.0.1"}
POST /mcp initialize         -> {"serverInfo":{"name":"tour-search","version":"0.0.1"}, ...}
POST /mcp tools/list         -> {"tools":[{"name":"list_regions", "inputSchema":{...},
                                        "outputSchema":{"properties":{"id","label","offers_count"}}}]}
POST /mcp tools/call         -> {"content":[{"type":"text","text":"not implemented: ..."}]}
```

The probe also proved the two findings above: `ModelContextProtocol` alone has no `MapMcp` (the build
fails to find it), and `outputSchema` is camelCase until `JsonPropertyName` is applied. The probe and its
process were deleted; no port remains bound.

### Startup shape

```
Program.cs
├── read configuration (URL, MCP path, TOURSEARCH_DB_*)   → fail fast, exit 1
├── builder.Services.AddInfrastructure()                   → no hosted service (ADR 004)
├── AddMcpServer(...) .WithHttpTransport(...) .WithTools<...>()
├── app.MapMcp("/mcp"); app.MapGet("/health", ...)
└── try { await app.RunAsync(); return 0; }
    catch (config)  { return 1; }
    catch (fatal)   { return 2; }
```

Exit codes must be returned from `Main`, not set via `Environment.Exit` deep in the stack, so `Console`
flushing and disposal stay ordered (`docs/conventions.md#cancellation`: never block shutdown; use
`await`).

### File plan

```
src/TourSearch.Api/
├── Program.cs                  # startup, endpoints, exit codes
├── Tools/
│   ├── RegionTools.cs          # list_regions — stub
│   ├── SearchTools.cs          # search_tours, get_search_results — stubs
│   └── Contracts/              # response records with JsonPropertyName (snake_case)
└── appsettings.json            # defaults only, no secrets

tests/TourSearch.UnitTests/
└── Api/ToolSurfaceTests.cs     # exactly three tools; schemas match the contract
```

## Hard rules (AGENTS.md §3)

| Rule | Applies | How the implementation respects it |
|---|---|---|
| `Api` must not depend on `Scraper` | **yes — directly at risk here** | the API is the process being built; no `Scraper` reference is added. ADR 001 |
| One `TourSearchDbContext` in `Infrastructure` | **yes** | the API consumes it through `AddInfrastructure()`; it defines no context and no `OnModelCreating` |
| Exactly one process starts `BackgroundService` (Worker) | **yes** | `AddInfrastructure()` registers none; a test asserts no TourSearch `IHostedService` is in the graph. ADR 004 |
| Job claim = atomic CAS | no | no job is created; `search_tours` is a stub. ADR 005 |
| Every persisted `DateTime` is UTC | **yes (indirect)** | the API writes nothing, but any timestamp in a response record must be documented as UTC and produced as UTC, never `DateTime.Now` |
| Transient error must not permanently fail a job | no | no job lifecycle here |
| Partial parse = `Completed` + `Degraded` + `Warnings` | **yes (contract only)** | the `get_search_results` response shape must be able to express `degraded` and `warnings`, even as a stub, so the contract is not under-modelled |
| `get_search_results` is paginated | **yes (contract only)** | the input must carry `limit`/`cursor` and the output `next_cursor`; the stub returns no unbounded list, and `limit` default 20 / max 50 are documented in the schema |
| No secrets in committed files, including examples | **yes** | `appsettings.json` carries defaults only; no credential, no fake literal. ADR 003 |
| No test/CI contacts TourVisor.ru | **yes** | no test contacts anything; the only HTTP calls are to `127.0.0.1` |
| No UA rotation / proxy / anti-bot evasion | no | nothing scraping-related |
| `Singleton` only for shared state; default `Scoped` | **yes** | the MCP server and data source are shared state; tools resolve the `DbContext` as `Scoped` |
| `CancellationToken` propagates through every layer | **yes** | every tool method takes a `CancellationToken` and passes it on, even as a stub — retrofitting it later is the mistake this rule prevents |

## Edge cases and error handling

| Case | Expected behaviour | `ErrorCode` | `Retryable` |
|---|---|---|---|
| A required `TOURSEARCH_DB_*` variable is missing | exit `1`, error names the variable | n/a | n/a |
| The configured URL/port is already in use | exit `1` with a clear bind error, not a silent hang | n/a | n/a |
| Unknown tool name in `tools/call` | protocol-level JSON-RPC error, **not** a tool result (`mcp-tools.md#errors--two-different-layers`) | `-32602` | n/a |
| Malformed JSON-RPC request | protocol-level error object | `-32602` | n/a |
| A tool is called with wrong argument types | protocol/validation error; the stub does not invent a value | `-32602` | n/a |
| `list_regions` called with `query` absent | valid call; the stub still explains it is not implemented | n/a | n/a |
| `get_search_results` called with an unknown `job_id` | **stub: not implemented.** Once implemented this becomes a tool-level `JOB_NOT_FOUND` — the stub must not pretend to look it up | n/a | n/a |
| `search_tours` called | **no job is created**; the stub says so | n/a | n/a |
| The process receives `SIGTERM` | shuts down cleanly, exit `0`; no lease exists for the API to release | n/a | n/a |
| The database is unreachable | `tools/list` still works (stubs touch no DB); the failure surfaces when a tool is implemented | n/a | n/a |
| A fourth tool is added accidentally | a test fails, because the contract is exactly three | n/a | n/a |

Cancellation: every tool signature takes `CancellationToken` and forwards it. Cancellation must not be
swallowed (`docs/conventions.md#cancellation`). The API holds no lease, so cancellation is a plain
shutdown.

## State transitions

`Не применимо — сервер не создаёт job и не пишет статусы. Все три инструмента — заглушки.`

## Docs to update

| File | What changes | Required by |
|---|---|---|
| `docs/mcp-tools.md` | the `Version` row `[PLANNED]` → the real version; record that the endpoint path is `/mcp`; if the transport's statelessness is confirmed, state it as a fact | FR-002, FR-003 |
| `docs/runtime.md` | `Ports and health`: the `/health` response fields that were implemented, and the MCP path | FR-001 |
| `docs/architecture.md` | note that HTTP hosting needs the separate `ModelContextProtocol.AspNetCore` package, so nobody re-pins the wrong one | FR-003 finding |
| `docs/agents.md` | §2 step 8 gate marked as partially satisfied (hosting done, tool logic pending) | — |
| `docs/open-questions.md` | only if something closes; auth (#2) and polling (#1) stay open | — |

## ADRs required

| ADR | Action | Context |
|---|---|---|
| new `docs/adr/043-mcp-hosted-over-streamable-http.md` | **create** | the transport decision already recorded as `[DECIDED]`, now with the *how*: the separate AspNetCore package, the pinned version, the `/mcp` path, and stateless operation. This is the piece that is currently unreachable in the project file |
| new `docs/adr/044-tools-ship-as-stubs-first.md` | **create** | why the contract surface ships before the behaviour: it makes the contract testable and prevents a half-working tool from being mistaken for a finished one. Records the deliberate choice the user made |
| [ADR 041](../../docs/adr/041-no-automatic-migration-on-startup.md) | unchanged | still holds; the API must not migrate on start |

If either feels like ceremony, say so — but ADR 043 records a decision that a future reader would
otherwise repeat the wrong way (referencing `ModelContextProtocol` and finding no `MapMcp`).

## Verification

```bash
dotnet build TourSearch.sln
dotnet test TourSearch.sln --verbosity minimal
```

> `[FACT]` `--all-warnings-as-errors` and `--summary=brief` are **not** real .NET 10 SDK options
> (`MSB1001`). Warnings-as-errors comes from `TreatWarningsAsErrors` in `Directory.Build.props`.

Start the server and exercise the contract over the wire:

```bash
export ASPNETCORE_URLS=http://127.0.0.1:5080

dotnet run --project src/TourSearch.Api &
curl -s http://127.0.0.1:5080/health

# initialize — MUST use a revision that supports the handshake.
# 2026-07-28 deliberately removed it: that revision is rejected with -32022, and that
# rejection is the CORRECT behaviour, not a failure. See "Handshake behaviour" in
# docs/mcp-tools.md and ADR 043.
curl -s -X POST http://127.0.0.1:5080/mcp \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"curl","version":"1"}}}'

# the handshake-free path: 2026-07-28 needs no initialize at all
curl -s -X POST http://127.0.0.1:5080/mcp \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'

# tools/list — compare field for field with docs/mcp-tools.md
curl -s -X POST http://127.0.0.1:5080/mcp \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'

# each stub, verifying it invents nothing
curl -s -X POST http://127.0.0.1:5080/mcp \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_regions","arguments":{}}}'
```

Manual MCP contract check (`AGENTS.md` §4) — **never against the live site**:

```bash
npx @modelcontextprotocol/inspector   # connect to http://127.0.0.1:5080/mcp
```

Expected: the inspector lists exactly three tools with schemas; each call returns the stub message.

No migration is involved:

```bash
# not applicable — the schema already exists from task 003
```

## Definition of done

1. `dotnet build TourSearch.sln` passes with 0 warnings.
2. Tests pass **and actually run** — a non-zero passed count is quoted. No test contacts the network
   beyond `127.0.0.1`, and none leaves a port bound.
3. No DB state is involved: no job is created, no status written, no migration applied at startup.
4. `docs/mcp-tools.md` matches the implementation **field for field, including `outputSchema`** — the
   response of `tools/list` is quoted next to the contract and the diff is empty. This is the criterion
   that actually defines the task.
5. Every decision made here is an ADR: 043 and 044 created and referenced from the docs they affect.

## Repo drift found

| Claim | Where | Reality |
|---|---|---|
| `ModelContextProtocol` 2.1.0 provides the Streamable HTTP transport | `src/TourSearch.Api/TourSearch.Api.csproj` comment | `[FACT]` it does not — no `MapMcp`/`WithHttpTransport` in 2.1.0 **or** 2.2.0. HTTP hosting is the separate `ModelContextProtocol.AspNetCore` package. The project cannot express its own documented transport |
| SDK pinned at `2.1.0` | `src/TourSearch.Api/TourSearch.Api.csproj` | `[FACT]` `2.2.0` is the latest stable of all three packages |
| Protocol revision `2026-07-28` | `docs/mcp-tools.md` | `[FACT]` **correct** — the SDK knows the string and the spec page returns 200. Not stale |
| `Version` row marked `[PLANNED]` | `docs/mcp-tools.md` server metadata | still true at authoring time; FR-003/DoD 4 make it real |
| `/health` returns "server metadata (name, version, protocol revision)" | `docs/runtime.md#ports-and-health` | unimplemented; this task implements it |
| `Api/Program.cs` is a placeholder | `AGENTS.md` §1 | still true; this task replaces it |
| `Api` never starts a `BackgroundService` | `AGENTS.md` §3, ADR 004 | must remain true; a test now enforces it |

## Open questions

- **Endpoint path `/mcp`.** The docs never name a path. → proposed default: **`/mcp`**, the SDK's
  conventional mapping, documented in `docs/runtime.md` and `docs/mcp-tools.md` → blocks: nothing.
- **Stateless vs stateful transport.** The protocol revision `2026-07-28` removes protocol-level sessions
  (`docs/mcp-tools.md`), which suggests `Stateless = true`; the probe used it successfully. → proposed
  default: **stateless**, because there is no session state to keep and it matches the revision →
  blocks: nothing, but confirm against the revision's text during implementation.
- **Which protocol revision to advertise.** The SDK knows `2025-11-25` and `2026-07-28`. → proposed
  default: **let the SDK negotiate**, and assert only that `tour-search` is named and the revision is one
  the SDK supports → blocks: nothing.
- **`appsettings.json` for the URL.** `docs/runtime.md` does not name a variable for it. → proposed
  default: **standard ASP.NET Core `ASPNETCORE_URLS`** with a documented default, rather than inventing a
  `TOURSEARCH_*` name → blocks: nothing.
- **Where the stub's "not implemented" text lives.** → proposed default: **in the tool response**, not in
  a log line, because the caller is an agent and must see it → blocks: nothing.
