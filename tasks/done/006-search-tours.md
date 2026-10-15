# 006 — `search_tours` creates a job

| | |
|---|---|
| **Status** | `done` |
| **Type** | feature |
| **Created (UTC)** | `2026-10-15` |
| **Affected projects** | `TourSearch.Core`, `TourSearch.Infrastructure`, `TourSearch.Api`, `tests/TourSearch.UnitTests` |
| **Source request** | > «сразу search_tours» |

## Summary

Make `search_tours` real: validate the parameters, canonicalise them into a `ParametersHash`, dedupe
against a recent `Completed` job, enforce the global active-job limit, and create a `Pending` job —
returning the contract's five fields. No scraping happens; the Worker does not exist yet, so a created job
stays `Pending` by design.

This closes the deferred obligation from [ADR 038](../../docs/adr/038-parameter-validation-at-tool-layer.md)
("parameter ranges are enforced at the MCP tool layer") and makes the error-contract's **tool-level error**
layer real for the first time.

## Problem

`search_tours` is a stub (ADR 044). Nothing can create a job, so `get_search_results` could never have
anything to read, and the MCP contract is only half reachable. Three things the contract depends on do not
exist at all:

1. **No canonicalisation.** `ParametersHash` is specified as "SHA-256 over canonical JSON of
   `SearchParameters`: sorted keys, normalized region, ISO-8601 dates, explicit currency, no
   locale-dependent number formatting" (`docs/domain.md#identity-and-dedupe`) — and **no code computes it**.
   Without it there is no dedupe, and ADR 012's "no `idempotency_key`" decision has no mechanism at all.
2. **No validation anywhere.** ADR 038 decided the tool layer owns the ranges, and deliberately left the
   entities dumb. That gap is closed here, and it is the only thing standing between a malformed request
   and a `Completed` job with 0 offers — indistinguishable from a genuinely empty result.
3. **No tool-level error path.** `PARAM_INVALID` is "tool-level only, never persisted", and the current
   tools only ever return normal results. The layer specified in `docs/mcp-tools.md#errors--two-different-layers`
   is unimplemented.

## Scope

### In scope

- Validation of every parameter, returning a **tool-level** error (`isError: true` + structured content)
  with `PARAM_INVALID` — never a protocol error.
- Canonical JSON + SHA-256 `ParametersHash`, deterministic across processes and locales.
- Dedupe: reuse a `Completed` job whose hash matches within a **24-hour reuse window**
  (ADR 047 — distinct from the 30-day job TTL).
- Global active-job limit (default **10**), computed by query, with a **new** `RATE_LIMITED` tool-level
  code (ADR 048).
- Creating the `SearchJob` row with `Pending` status and the contract's five output fields.
- Tests: validation table, canonicalisation stability, dedupe behaviour, limit behaviour, contract shape.

### Out of scope

- **Scraping and the Worker.** Nothing executes the job (`AGENTS.md` §2 step 7). A created job stays
  `Pending`; no `InProgress`, no offers. This must be visible, not implied.
- **`get_search_results`.** It stays a stub; reading a job is a separate task, and the two should ship
  together to be useful to an agent.
- **Auth** — open question 2. The rate limit is therefore **global** and must be described as global, not
  "per caller" (`docs/mcp-tools.md#rate-limiting` explicitly forbids presenting it as per-agent).
- **Region catalogs beyond Turkey** — the catalog already covers what `search_tours` accepts.
- **Changing the MCP response shape.** The stub's `outputSchema` for `search_tours` is already correct.
- **`currency` beyond `RUB`.** `docs/domain.md#currency` says `[DECIDED] RUB for now`; a different currency
  is rejected rather than silently stored.
- Frontend, booking, payment, tourist PII, anti-bot evasion — `AGENTS.md` §8.

## Requirements

### Functional

**FR-001 — every parameter is validated before anything is persisted**

Rejected with a **tool-level** error carrying `PARAM_INVALID` and naming the offending field:

| Field | Rule |
|---|---|
| `region` | required, non-empty, and **must exist in the catalog and be `Active`** |
| `adults` | required, `>= 1` |
| `children` | `>= 0` |
| `hotel_stars` | `0..5` (`0` = any/unknown) |
| `start_date` | required, ISO-8601 date, **not in the past** (day precision, UTC) |
| `nights` | required, `>= 1` |
| `price_min` | `>= 0` when present |
| `price_max` | `>= price_min` when both present |
| `currency` | must be `RUB` today; anything else is rejected, not stored |

- Validation happens **before** any insert, so a rejected request leaves **no** row.
- The error message names the field and the rule; the structured content carries a machine-readable code so
  an agent can correct itself without parsing prose.
- `PARAM_INVALID` is **never persisted** (`docs/domain.md#error-taxonomy`).

*Acceptance:* a table-driven test asserts each rule rejects with `PARAM_INVALID` and the field name; an
integration-style check confirms no `search_jobs` row is created for a rejected request.

**FR-002 — `ParametersHash` is canonical, stable and locale-independent**

- SHA-256 over canonical JSON: **sorted keys**, normalized region (the slug exactly as `list_regions`
  returns it), ISO-8601 dates, explicit currency, invariant number formatting.
- Format: `sha256:` + lowercase hex (matching the documented prefix and the column's 71-char width).
- The same logical request produces the **same hash** regardless of: JSON key order in the request, the
  process, the machine locale, and culture-specific number/date formatting.
- Two requests differing in **any** parameter differ in hash.
- `[DECIDED]` the hash includes `CurrencyCode`, which lives on `SearchJob`; `SearchParameters` has no such
  field and is not extended (verified: it has exactly 8 fields).
- `start_date` contributes only the date, no time component.

*Acceptance:* a test asserts equality across shuffled key order and across `tr-TR`/`en-US`/`ru-RU` cultures;
a test asserts a change in each of the 9 parameters changes the hash.

**FR-003 — dedupe reuses a recent `Completed` job**

- If a `Completed` job with the same `ParametersHash` exists with `CompletedAt` inside the **24-hour reuse
  window**, return it with `reused: true` and its existing `job_id`.
- Otherwise create a new job and return `reused: false`.
- A `Completed` job **outside** the window is not reused — a new job is created
  (ADR 047: freshness is not the same as retention).
- `Pending`, `InProgress`, `Failed` and `Cancelled` jobs are **never** reused
  (`docs/domain.md#identity-and-dedupe` leaves `InProgress`/`Failed` `[OPEN]`; this task keeps them out
  rather than deciding them in passing).
- The window is configurable; `24` hours is the default.

*Acceptance:* a `Completed` job inside the window is reused; the same job aged past the window is not; a
`Pending` job with a matching hash is not reused; a `Failed` job is not reused.

**FR-004 — the global active-job limit is enforced and honestly described**

- Before creating a job, count `search_jobs WHERE status IN ('Pending','InProgress')` — **computed, never a
  stored counter** (`docs/mcp-tools.md#rate-limiting`), served by the existing `(status, created_at)` index.
- At or above the limit (**default 10**), reject with a **tool-level** error, code `RATE_LIMITED`.
- The rejection is a tool-level error, **not** protocol, and **not** a `Failed` job — no row is created.
- The tool description and the error message state that the limit is **global**, not per-caller.
- The limit is configurable and readable from configuration.
- A **reused** job does not count against the limit and is not blocked by it: reuse creates no new work.
  (Document this ordering explicitly — it is the difference between a working dedupe and one that fails
  under load.)

*Acceptance:* with the limit set to a small number, the (limit+1)-th distinct request is rejected with
`RATE_LIMITED` and creates no row; a request that would **reuse** a `Completed` job still succeeds at the
limit.

**FR-005 — the created job is correct and the response matches the contract**

- The row is created with: `Id` (new `Guid`), `ParametersHash`, `Parameters` (the JSONB complex type),
  `CurrencyCode`, `Status = Pending`, `Attempt = 0`, `CreatedAt` (UTC), `Degraded = false`,
  `Warnings` empty, `OffersCount = 0`, `PagesDone = 0`, `PagesTotal = null`, all lease/error fields null.
- `LeaseToken`, `LeaseExpiresAt`, `StartedAt`, `CompletedAt`, `CancelRequestedAt` are **not** set — the
  job is not claimed (ADR 005: claiming is the Worker's CAS).
- Output is exactly `job_id`, `status`, `reused`, `parameters_hash`, `poll_after_ms`.
- `status` is `"Pending"` as the contract's enum string.
- `poll_after_ms` is a **configurable constant**, default `5000`.
- **Every persisted `DateTime` is UTC** (`docs/database.md#utc-trap`).
- The stub text and the "NOT IMPLEMENTED" description suffix are removed **for this tool only**;
  `get_search_results` keeps them. The existing per-tool assertion in `ToolSurfaceTests` is updated to
  match the new set.

*Acceptance:* a test asserts every field of the created row; the response is compared field for field with
`docs/mcp-tools.md`; the tool is called over real HTTP and the response quoted.

**FR-006 — the tool is honest about the missing Worker**

- The tool's description states that a created job stays `Pending` until a worker picks it up, and that
  **no worker exists in this build** — so the agent does not wait for a result that will never arrive.
- The `instructions` string carries the same caveat while the Worker is missing.
- This text is removed when the Worker ships.

*Acceptance:* the description and instructions are read back from `tools/list` and quoted.

### Non-functional

**NFR-001 — validation lives in one place, and the entities stay dumb**

The ranges are enforced **once**, in the tool layer (ADR 038). `SearchParameters` gains no
`IValidatableObject`, no constructor checks, and the database gains no `CHECK` constraint. A second copy
of a rule is a defect: the two drift and one of them silently wins.

**NFR-002 — the limit check and the insert are not racy in a way that matters**

Two concurrent requests can each observe `count = limit - 1` and both insert, yielding `limit + 1` active
jobs. This is **accepted**, because the limit is a coarse load-shedding guard, not a correctness
invariant, and `docs/mcp-tools.md#rate-limiting` already rejects a stored counter (which would need the
same CAS discipline as the job claim). The task records the acceptance rather than adding a transaction
that would serialise every `search_tours` call — but the **reuse** path must be checked **before** the
limit, so a dedupe under load is never rejected by a counter it does not increment.

**NFR-003 — no network, no scraping**

The tool writes a row and returns. No HTTP call to TourVisor, no Playwright, no Worker. `Api` still
references no `Scraper` (ADR 001).

## Technical design

### Affected projects and dependency direction

| Project | Change | Does it break `Api` ↛ `Scraper`? |
|---|---|---|
| `TourSearch.Core` | `JobErrorCode` gains `RATE_LIMITED`; parameter canonicalisation | no — still POCO/BCL only |
| `TourSearch.Infrastructure` | job repository (create, dedupe lookup, active count) | no |
| `TourSearch.Api` | real `search_tours`; tool-level error path | no |
| `tests/TourSearch.UnitTests` | canonicalisation, validation, dedupe, limit, contract | no |

### Canonicalisation

```
SearchParameters + CurrencyCode
        │  sorted keys, invariant formatting, ISO-8601 date-only
        ▼
canonical JSON ── SHA-256 ──▶ "sha256:" + lowercase hex      (71 chars, fits the column)
```

The canonical form must be built explicitly (a stable key order and invariant formatting), **not** by
serialising the entity with a default serialiser: property order and culture leakage are exactly the two
ways the same request would produce two different hashes and the dedupe would silently stop working.

### Validation result shape

Tool-level error, per `docs/mcp-tools.md#errors--two-different-layers`:

```
isError: true
structuredContent: { error_code: "PARAM_INVALID", field: "adults", message: "..." }
```

This is a **result**, not a JSON-RPC `error` object. Getting this wrong is the documented failure mode:
`-32602` is for malformed requests, not for "`adults` must be at least 1".

### Order of operations

```
1. validate parameters                        → PARAM_INVALID (no row)
2. canonicalise → ParametersHash
3. dedupe: recent Completed with this hash?   → return reused: true
4. count active jobs                          → RATE_LIMITED (no row)
5. insert Pending job
6. return job_id / status / reused / parameters_hash / poll_after_ms
```

Steps 3 before 4 is deliberate (NFR-002): reuse must never be blocked by the cap it does not consume.

### Configurable values

| Setting | Default | Why configurable |
|---|---|---|
| Reuse window | 24 hours | freshness vs. reuse is a tuning decision (ADR 047) |
| Active-job limit | 10 | load-shedding guard, depends on worker throughput |
| `poll_after_ms` | 5000 | should track the real pickup latency |

### File plan

```
src/TourSearch.Core/
├── Enums/JobErrorCode.cs                      # + RATE_LIMITED (tool-level)
├── Search/SearchRequest.cs                    # the 9 validated inputs
└── Search/ParameterHasher.cs                  # canonical JSON + SHA-256

src/TourSearch.Infrastructure/Jobs/
├── IJobRepository.cs
└── JobRepository.cs                           # create, find-reusable-by-hash, count-active

src/TourSearch.Api/Tools/
├── SearchTools.cs                             # real search_tours; get_search_results still a stub
└── ToolErrors.cs                              # tool-level error result helper

tests/TourSearch.UnitTests/Search/
├── ParameterHasherTests.cs
├── SearchValidationTests.cs
├── SearchDedupeTests.cs
└── SearchResponseContractTests.cs
```

## Hard rules (AGENTS.md §3)

| Rule | Applies | How the implementation respects it |
|---|---|---|
| `Api` must not depend on `Scraper` | **yes** | the tool writes a row only; no Playwright, no HTTP to the source |
| One `TourSearchDbContext` in `Infrastructure` | **yes** | the job repository uses the existing context; no new context, no model change beyond the enum |
| Exactly one process starts `BackgroundService` (Worker) | **yes** | nothing is executed; no hosted service is added to the API. The job waits for the Worker (ADR 004) |
| Job claim = atomic CAS (`Pending → InProgress`, lease + attempt+1) | **yes — must NOT be done here** | the tool creates `Pending` and never claims. Claiming is the Worker's CAS; doing it here would double-claim. ADR 005 |
| Every persisted `DateTime` is UTC | **yes** | `CreatedAt` is UTC; `docs/database.md#utc-trap` means a non-UTC value throws at save |
| Transient error must not permanently fail a job | **yes** | a created job is `Pending`, never `Failed`; no error is persisted at creation |
| Partial parse = `Completed` + `Degraded` + `Warnings` | no | nothing is parsed; the fields are initialised correctly for the Worker |
| `get_search_results` is paginated | no | untouched (still a stub) |
| No secrets in committed files, including examples | **yes** | no credential is involved; config values are non-secret |
| No test/CI contacts TourVisor.ru | **yes** | no test contacts anything; `region` is validated against our own catalog |
| No UA rotation / proxy / anti-bot evasion | no | nothing scraping-related |
| `Singleton` only for shared state; default `Scoped` | **yes** | the job repository is `Scoped` |
| `CancellationToken` propagates through every layer | **yes** | tool → repository → EF; the count and insert are cancellable |

## Edge cases and error handling

| Case | Expected behaviour | Code | Layer |
|---|---|---|---|
| `region` unknown or inactive | rejected, names the region | `PARAM_INVALID` | tool |
| `region` is a **label** (`"Кемер"`) instead of a slug | rejected — labels are display text, never ids | `PARAM_INVALID` | tool |
| `adults = 0` / missing | rejected | `PARAM_INVALID` | tool |
| `nights = 0` | rejected | `PARAM_INVALID` | tool |
| `hotel_stars = 6` | rejected | `PARAM_INVALID` | tool |
| `hotel_stars = 0` | **accepted** — "any/unknown" by decision | — | — |
| `price_max < price_min` | rejected | `PARAM_INVALID` | tool |
| `price_min`/`price_max` absent | accepted; the hash reflects their absence | — | — |
| `start_date` in the past | rejected | `PARAM_INVALID` | tool |
| `start_date` with a time component | the date only is used; the hash carries no time | — | — |
| `currency = "USD"` | rejected — only `RUB` today | `PARAM_INVALID` | tool |
| `currency` absent | defaults to `RUB` (the `[DECIDED]` default) | — | — |
| `children` absent | defaults to `0` | — | — |
| Same request twice in the window, job `Completed` | second call returns the same `job_id`, `reused: true` | — | — |
| Same request, matched job is `Pending`/`InProgress` | **not** reused; a new job is created | — | — |
| Same request, matched job `Failed` | **not** reused; a new job is created | — | — |
| Matched `Completed` job older than the window | **not** reused; a new job is created | — | — |
| Limit reached, request would reuse | **reused** — reuse is checked first and consumes nothing | — | — |
| Limit reached, request is new | rejected, no row created | `RATE_LIMITED` | tool |
| Two concurrent requests at the boundary | both may insert (accepted, NFR-002) — a coarse guard, not an invariant | — | — |
| Malformed JSON-RPC body | protocol error `-32600`, **not** a tool error | `-32600` | protocol |
| Unknown tool name | protocol error `-32602` | `-32602` | protocol |
| Database unreachable | the call fails cleanly; it must **not** return a fake `job_id` | — | — |

Cancellation: tool → repository → EF query/insert. A cancelled request must not leave a partially created
job; the insert is a single EF operation, so cancellation either commits it or does not.

## State transitions

Only **one** transition is performed, and only into `Pending`:

```
(none) ──search_tours──▶ Pending
```

Everything else is the Worker's (ADR 005) and is **not** written here:

```
Pending ──claim──▶ InProgress ──▶ Completed | Failed | Pending(backoff)
Pending ──▶ Cancelled
InProgress ──(lease expiry)──▶ Pending
```

No `Attempt` increment, no lease, no `StartedAt`: the job is created unclaimed.

## Docs to update

| File | What changes | Required by |
|---|---|---|
| `docs/mcp-tools.md` | `search_tours` marked implemented; the 24h reuse window; the global limit and its default; `RATE_LIMITED` added to the tool-error table; the "no worker yet" note | FR-003, FR-004, FR-006 |
| `docs/domain.md` | `RATE_LIMITED` added to the taxonomy as tool-level/never-persisted; the reuse window distinguished from the job TTL | FR-004, ADR 047 |
| `docs/database.md` | the reuse lookup needs `(parameters_hash, status, completed_at)` or justify the existing `parameters_hash` index; the TTL section cross-references the reuse window | FR-003 |
| `docs/adr/038` | mark the deferred validation as now implemented | FR-001 |
| `docs/adr/044` | update the live/stub discriminator set again (`get_search_results` is now the only stub) | FR-005 |
| `docs/open-questions.md` | question 6 (caching/reuse) narrows: the `Completed` window is decided; `InProgress`/`Failed` reuse stays `[OPEN]` | FR-003 |

## ADRs required

| ADR | Action | Context |
|---|---|---|
| new `docs/adr/047-reuse-window-separate-from-ttl.md` | **create** | the reuse window (24h) is deliberately **not** the 30-day job TTL. Retention and freshness are different questions; a 20-day-old `Completed` job served as a fresh result is silent misinformation |
| new `docs/adr/048-rate-limited-tool-error-code.md` | **create** | `RATE_LIMITED` joins the taxonomy as a **tool-level, never-persisted** code, and the limit is global and computed. Records why a stored counter was rejected and the accepted non-atomicity (NFR-002) |
| new `docs/adr/049-parameter-canonicalisation.md` | **create** | the canonical JSON form is built explicitly, not by a default serialiser; records that changing it invalidates every stored hash and causes a wave of duplicate jobs |

If any feels like ceremony, say so — but ADR 049 in particular guards a value that is invisible in code
review and whose change is silently destructive.

## Verification

```bash
dotnet build TourSearch.sln
dotnet test TourSearch.sln --verbosity minimal
```

> `[FACT]` `--all-warnings-as-errors` and `--summary=brief` are **not** real .NET 10 SDK options (`MSB1001`).

The tool over real HTTP (the API validates `selectors.json` at startup, so run from the repo root):

```bash
export ASPNETCORE_URLS=http://127.0.0.1:5080
set -a; . ./LocalSecrets.env; set +a
dotnet run --project src/TourSearch.Api &

# a valid request
curl -s -X POST http://127.0.0.1:5080/mcp -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search_tours","arguments":
      {"region":"kemer","adults":2,"start_date":"2026-06-15","nights":7}}}'

# invalid: adults = 0  -> isError, PARAM_INVALID, field named, NOT a -32602
# unknown region       -> isError, PARAM_INVALID
# NOTE: repeating the request here does NOT exercise reuse. FR-005 creates a Pending job,
# FR-003 refuses to reuse Pending, and no worker exists to move it to Completed - so the
# repeat correctly returns a NEW job with reused:false. That is the reachable behaviour.
# To exercise the reuse path, complete the row with a fixture statement first:
#   update tour_search.search_jobs set status='Completed', completed_at=now() where id='<job_id>';
# then repeat the identical request -> same job_id, reused: true.
# Move completed_at 25 hours back and repeat once more -> a NEW job (the window is real).
```

Then verify **on the server**:

```sql
select id, status, attempt, currency_code, created_at,
       lease_token is null as no_lease, parameters_hash
  from tour_search.search_jobs order by created_at desc limit 5;
select count(*) from tour_search.search_jobs;   -- no row from any rejected request
```

Expected: created jobs are `Pending`, `attempt = 0`, `no_lease = true`, `created_at` is UTC,
`parameters_hash` is `sha256:`-prefixed and 71 chars; a rejected request adds **no** row.

Clean up any jobs created during verification, so the task leaves no data behind.

MCP contract check: the `search_tours` entry in `tools/list` is unchanged in shape except that its
description loses the "NOT IMPLEMENTED" suffix and gains the reuse-window and global-limit notes.

## Definition of done

1. `dotnet build TourSearch.sln` passes with 0 warnings.
2. Tests pass **and actually run** — a non-zero passed count is quoted. No test contacts the network.
3. The DB state is reachable only through the documented transition: jobs appear as `Pending` and nothing
   writes any other status. No ad-hoc writes.
4. `docs/mcp-tools.md` matches the implementation field for field, `outputSchema` included — the
   `search_tours` output keeps exactly `job_id`, `status`, `reused`, `parameters_hash`, `poll_after_ms`.
5. Every decision made here is an ADR: 047, 048 and 049 created and referenced from the docs they affect.

## Repo drift found

| Claim | Where | Reality |
|---|---|---|
| "reuse within the TTL window" | `docs/domain.md#identity-and-dedupe` | conflates retention (30d TTL) with freshness; split by ADR 047. The doc is corrected |
| `PARAM_INVALID` is tool-level and never persisted | `docs/domain.md#error-taxonomy` | true, but **no tool could return it** until now — the error layer was unreachable. FR-001 implements it |
| parameter ranges enforced at the tool layer | `docs/adr/038` | decided, not implemented; this task closes it |
| `RATE_LIMITED` | not in `docs/domain.md#error-taxonomy` | absent from the 12-code taxonomy; ADR 048 adds it as tool-level |
| `search_tours` is a stub | `docs/adr/044`, `SearchTools.cs` | this task makes it real; `get_search_results` becomes the only stub |
| no canonicalisation code exists | implied by `docs/domain.md` | `[FACT]` verified: `ParametersHash` is declared and configured as a column, but nothing computes it |
| the rate limit is global until auth exists | `docs/mcp-tools.md#rate-limiting` | already documented; the default value (10) is chosen here |

## Open questions

- **`(parameters_hash, status, completed_at)` index.** The dedupe lookup filters on hash + status and
  orders by completion time; today only `parameters_hash` is indexed. → proposed default: **rely on the
  existing `parameters_hash` index** and let the status/date filter run over its (tiny) result set; add a
  composite index only if it measurably matters, since the table is empty → blocks: nothing.
- **Limit default of 10.** Not specified anywhere. → proposed default: **10, configurable**, chosen as a
  coarse guard while one Worker does not exist → blocks: nothing.
- **Reuse of `InProgress` / `Failed` jobs.** Explicitly `[OPEN]` in `docs/domain.md`; this task keeps both
  out → proposed default: **do not reuse either**, leaving the question open rather than deciding it in
  passing → blocks: nothing.
- **`start_date` "not in the past" — compared against which day?** Day precision in UTC. → proposed
  default: **reject when the date is strictly before today's UTC date**, so today is valid → blocks:
  nothing.
- **Whether a `Cancelled` job blocks reuse of an older `Completed` one.** → proposed default: **no** — the
  lookup selects the most recent eligible `Completed` job regardless of other rows → blocks: nothing.

## Outcome

| | |
|---|---|
| **Completed (UTC)** | `2026-10-15` |
| **Commit** | `0f3a649` — `feat: make search_tours create a job (tasks/006)` |
| **Acceptance** | `FR-001`–`FR-006`, `NFR-001`–`NFR-003` pass; build 0/0; `dotnet test` **145 passed, 0 failed** (+50); verified on the real DB and over real HTTP |
| **ADRs** | [047](./../../docs/adr/047-reuse-window-separate-from-ttl.md), [048](./../../docs/adr/048-rate-limited-tool-error-code.md), [049](./../../docs/adr/049-parameter-canonicalisation.md) created; [038](./../../docs/adr/038-parameter-validation-at-tool-layer.md) and [044](./../../docs/adr/044-tools-ship-as-stubs-first.md) updated |
| **Left open** | `get_search_results` is the only stub; no Worker, so jobs stay `Pending`; auth |

### Evidence

| Criterion | Check | Result |
|---|---|---|
| `FR-001` | real HTTP `adults=0` | `isError: true`, `PARAM_INVALID`, `field: "adults"` — a **result**, not `-32602` (contrasted against an unknown tool, which is `-32602`) |
| `FR-001` | unknown region | `PARAM_INVALID`, `field: "region"` |
| `FR-002` | `ParameterHasherTests` | equal across shuffled key order and `tr-TR`/`en-US`/`ru-RU`; each of the 9 params changes the hash |
| `FR-003` | fixture-completed job | reused inside the window; the same job aged 25 h → a new job |
| `FR-004` | `Search__ActiveJobLimit=1` | 2nd distinct request → `RATE_LIMITED`, **no** row; an identical request still returned `reused: true` at the cap |
| `FR-005` | `psql` on the created row | `Pending`, `attempt=0`, `currency=RUB`, `hash_len=71` with `sha256:` prefix, all lease/error fields null, `timestamptz` |
| `FR-006` | `tools/list` description | quotes the 24 h window, the `GLOBAL` limit, `RATE_LIMITED`, `PARAM_INVALID`, and `NO WORKER EXISTS IN THIS BUILD` |
| `NFR-001` | validation only in the tool layer | no `IValidatableObject`, no `CHECK` constraint added |
| `NFR-002` | reuse checked before the limit | proved at the cap: reuse succeeded, a new job was refused |
| `NFR-003` | no network, no Worker | `Api` deps still show 0 matches for scraper/playwright |

Rejected requests created **no** row: 5 HTTP calls produced 3 rows, then 1, and the accounting matched every
time. All test data deleted afterwards (jobs 0, regions 85, offers 0) and no port left bound.

### A real contradiction the implementer found in this task

FR-001's edge-case table said an **absent** `price_min`/`price_max` is accepted while
`SearchParameters` stores them as **non-nullable** `decimal` — so the persisted row could not represent
what the request and the hash did. The task contradicted the docs I was writing against.

Resolved by making them `decimal?` (**null = unbounded**). The alternatives were rejected because both
encode the meaning in a sentinel and lie about it: `decimal.MaxValue` makes the stored row claim a bound
of 7.9e28 the caller never asked for, and `0` breaks the documented `PriceMax >= PriceMin` invariant
whenever only `price_min` is given. No migration was needed — `parameters` is a single `jsonb` column.
`docs/domain.md` and `EntityShapeTests` were updated in the same change.

### Corrected verification step in this task (my error)

The Verification section told the reader to repeat a request to observe `reused: true`. That path is
**unreachable** in this build: FR-005 creates a `Pending` job, FR-003 refuses to reuse `Pending`, and no
worker exists to complete it. The implementer reported this instead of silently working around it. The step
now documents the reachable behaviour and the fixture statement needed to exercise reuse.

### Not verified

- **Feature behaviour is not certified** — the quotes above are observations.
- **Reuse was proved with a SQL fixture** that sets `completed_at`; the application itself never writes
  `Completed`, because the Worker does not exist. The reuse path is therefore exercised at the repository
  and HTTP level, not end-to-end through a real job lifecycle.
- **Concurrency overshoot** (NFR-002) is reasoned about, not measured: no test drives concurrent calls at
  the limit.
- **Label/hash stability across PostgreSQL versions** is untested — the hash is computed in .NET, so the
  database cannot affect it, but this was not explicitly asserted.

### Left open

- **`get_search_results` is the only remaining stub.** It should ship with the Worker, since a `Pending` job
  is all it can ever report until something completes one.
- **No Worker** (`AGENTS.md` §2 step 7). Every created job stays `Pending`; the tool says so, and that text
  must be removed when the Worker lands.
- **Auth (question 2).** The limit is global and the description says so; when auth arrives both the limit
  and that text must change.
- **Reuse of `InProgress`/`Failed`** stays `[OPEN]`; both are excluded rather than decided here.
- **`Search:*` defaults are unmeasured** — 24 h, 10, 5000 ms are considered choices, not tuned values.
