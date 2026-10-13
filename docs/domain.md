# Domain model

Status legend: [agents.md](./agents.md#status-legend).

## Entities

### `SearchJob`

A tour-search task.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK. **Never order or paginate by it** — see [Guid ordering](#guid-ordering) |
| `ParametersHash` | `string` | canonical hash of params — dedupe/idempotency key, indexed |
| `Parameters` | `SearchParameters` | owned entity → JSONB |
| `CurrencyCode` | `char[3]` | `[DECIDED]` `RUB` for now; keep the field from day one |
| `Status` | `JobStatus` | `Pending`, `InProgress`, `Completed`, `Failed`, `Cancelled` |
| `Attempt` | `int` | how many times a worker claimed this job |
| `LeaseToken` | `string?` | who owns `InProgress` — concurrency guard |
| `LeaseExpiresAt` | `DateTime?` (UTC) | crash recovery — see [runtime.md](./runtime.md#restart-semantics) |
| `CreatedAt` | `DateTime` (UTC) | ordering column |
| `StartedAt` | `DateTime?` (UTC) | when the job was claimed |
| `CompletedAt` | `DateTime?` (UTC) | terminal state reached |
| `Degraded` | `bool` | `Completed` but partial results |
| `Warnings` | `List<string>` | PG `text[]` — non-fatal parse problems |
| `OffersCount` | `int` | lets the agent judge results without loading them |
| `ErrorCode` | `JobErrorCode?` | machine-readable — see [Error taxonomy](#error-taxonomy) |
| `ErrorMessage` | `string?` | human-readable, for the agent |
| `Retryable` | `bool?` | whether the failure is transient |
| `Offers` | `List<TourOffer>` | **navigation only** — EF Core reverse-FK accessor, not a column |

### `SearchParameters` (owned → JSONB)

| Field | Type |
|---|---|
| `Region` | `string` — e.g. `"Antalya"`; must come from `list_regions`, see [mcp-tools.md](./mcp-tools.md#list_regions) |
| `Adults` | `int` ≥ 1 |
| `Children` | `int` ≥ 0 |
| `HotelStars` | `int` 1..5, `0` = any/unknown — see [HotelStars](#hotelstars) |
| `StartDate` | `DateTime` (UTC, day precision) — see [Date types](#date-types) |
| `Nights` | `int` ≥ 1 |
| `PriceMin` | `decimal(12,2)` |
| `PriceMax` | `decimal(12,2)` ≥ `PriceMin` |

### `TourOffer`

Found tour offer.

| Field | Type |
|---|---|
| `Id` | `Guid` |
| `SearchJobId` | `Guid` (FK) |
| `HotelName` | `string` |
| `HotelStars` | `int` 1..5, `0` = unknown |
| `Luxury` | `bool` | TourVisor "5+" / "deluxe" band — see [HotelStars](#hotelstars) |
| `Region` | `string` |
| `CheckIn` | `DateTime` (UTC, day precision) |
| `Nights` | `int` |
| `Price` | `decimal(12,2)` |
| `CurrencyCode` | `char[3]` |
| `RoomType` | `string?` |
| `MealType` | `string?` | normalized code (`AI`, `BB`, `HB`) + raw text kept in `Raw` |
| `SourceUrl` | `string?` |
| `ScrapedAt` | `DateTime` (UTC) |
| `Raw` | `Jsonb` `[PLANNED]` | untouched scraped payload, for parser regression and future fields |

## Types that need explicit decisions

### Date types

`DateOnly` **is not a C# type** `[FACT]`. Do not write it into the model.

- C#: `DateTime` in UTC, day precision by convention.
- PostgreSQL: `date` vs `timestamptz`. EF Core Npgsql maps `DateTime` → `timestamptz`. Mapping to `date` needs a custom column type.
- `[DECIDED]`: store `timestamptz` UTC, treat as date-only by convention. A real `[OPEN]` question: whether `CheckIn` needs a time component at all.

### Guid ordering

C# `Guid` → PostgreSQL `uuid`. A v4 UUID has **no ordering**, so `ORDER BY id` and offset pagination over it are meaningless and unstable.

- `[DECIDED]`: keep `uuid` (v4) as PK, order and paginate by `(created_at, id)` cursor.
- Alternative if chronological ids are genuinely needed: `uuid7()` (PostgreSQL 15.4+/16+) or client-generated uuid7. `[OPEN]` — verify your PostgreSQL version before adopting.

### HotelStars

Real hotel ratings are not only 3/4/5.

- `[DECIDED]`: `int` 1..5, `0` = unknown/any.
- TourVisor luxury band ("5+", deluxe) → `HotelStars = 5` + `Luxury = true`.
- `[OPEN]`: whether the agent should be able to filter by `Luxury` at all.

### Currency

A bare `decimal` price is ambiguous the moment a second source or currency appears.

- `[DECIDED]`: `CurrencyCode` on both `SearchParameters` and `TourOffer`, default `RUB`.
- Validation: reject a job whose currency differs from the source currency.

## Identity and dedupe

- `ParametersHash` = SHA-256 over canonical JSON of `SearchParameters`: sorted keys, normalized region (same form as `list_regions` returns), ISO-8601 dates, explicit currency, no locale-dependent number formatting.
- `[DECIDED]`: two agents asking the same params within the TTL window reuse the existing `Completed` job — the second call returns the same `job_id`. This is the idempotency guarantee the MCP contract relies on.
- `[OPEN]`: reuse of an `InProgress` job (races with cancellation) and of a `Failed` job.
- `[OPEN]`: dedupe of `TourOffer` across jobs. Within one job, offers are deduped by `SourceUrl`.

## Statuses `SearchJob`

```
                     ┌───────────────────────────────┐
                     │  lease expired (crash recovery)│
                     ▼                               │
Pending ──claim──▶ InProgress ──┬──▶ Completed  (Degraded possible)
   ▲                            ├──▶ Failed    (Retryable = false)
   │                            └──▶ Pending   (Retryable = true, backoff, Attempt+1)
   └── (backoff)
Pending ──▶ Cancelled   (explicit cancel_search by the agent)
```

- `Pending` — created, waiting for a worker.
- `InProgress` — claimed; `LeaseToken`, `LeaseExpiresAt`, `StartedAt`, `Attempt` are set.
- `Completed` — finished; `OffersCount` may be 0 (a legitimate empty result) or partial (`Degraded = true`).
- `Failed` — terminal error; `ErrorCode` + `ErrorMessage` + `Retryable`.
- `Cancelled` — the agent no longer wants the results.

`Completed` with 0 offers is **not** an error. Distinguishing "no matches" from "scraping failed" is the whole reason `Failed` and `Completed` are separate.

## Error taxonomy

`ErrorCode` is machine-readable so the agent can decide whether to retry; `ErrorMessage` is for humans.

| Code | Retryable | Meaning |
|---|---|---|
| `NETWORK_TIMEOUT` | yes | navigation exceeded the timeout |
| `NETWORK_UNREACHABLE` | yes | DNS/TLS/connection failure |
| `HTTP_429` | yes | source throttling — back off harder |
| `HTTP_5XX` | yes | source-side failure |
| `BROWSER_CRASH` | yes | Playwright/Chromium died |
| `LEASE_EXPIRED` | yes | worker died mid-job |
| `SELECTOR_NOT_FOUND` | no | required selector missing — markup changed |
| `PAGE_STRUCTURE_CHANGED` | no | item container parsed, fields not |
| `ANTI_BOT_DETECTED` | no | CAPTCHA / block page — see [scraping.md](./scraping.md#anti-bot) |
| `PARAM_INVALID` | n/a | rejected before the job exists — see [mcp-tools.md](./mcp-tools.md#errors) |
| `INTERNAL` | depends | bug; retry only after a fix |

## C# draft

```csharp
public class SearchJob
{
    public Guid Id { get; set; }
    public string ParametersHash { get; set; } = "";
    public SearchParameters Parameters { get; set; } = null!;
    public JobStatus Status { get; set; }
    public int Attempt { get; set; }
    public string? LeaseToken { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool Degraded { get; set; }
    public List<string> Warnings { get; set; } = new();
    public int OffersCount { get; set; }
    public JobErrorCode? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool? Retryable { get; set; }

    // navigation only — reverse FK accessor, not a persisted column
    public List<TourOffer> Offers { get; set; } = new();
}

public enum JobStatus { Pending, InProgress, Completed, Failed, Cancelled }
```
