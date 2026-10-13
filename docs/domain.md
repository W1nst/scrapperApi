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
| `CancelRequestedAt` | `DateTime?` (UTC) | cancel requested while the job was already `InProgress`; the Worker releases the lease at the next navigation boundary — see [cancel_search](./mcp-tools.md#cancel_search-optional) |
| `CreatedAt` | `DateTime` (UTC) | ordering column |
| `StartedAt` | `DateTime?` (UTC) | when the job was claimed |
| `CompletedAt` | `DateTime?` (UTC) | terminal state reached |
| `Degraded` | `bool` | `Completed` but partial results |
| `Warnings` | `List<string>` | non-fatal parse problems — EF Core primitive collection; on Npgsql this is a native PG `text[]` column, not JSON (see [database.md](./database.md#warnings-column-representation)) |
| `OffersCount` | `int` | **running** counter, incremented as offers are persisted; authoritative at a terminal status. Lets the agent judge results without loading them |
| `PagesDone` | `int` | running counter — pages parsed successfully |
| `PagesTotal` | `int?` | expected page count; `null` when the source reports no total |
| `ErrorCode` | `JobErrorCode?` | machine-readable — see [Error taxonomy](#error-taxonomy) |
| `ErrorMessage` | `string?` | human-readable, for the agent |
| `Retryable` | `bool?` | whether the failure is transient |
| `Offers` | `List<TourOffer>` | **navigation only** — EF Core reverse-FK accessor, not a column |

### `SearchParameters` (owned → JSONB)

| Field | Type |
|---|---|
| `Region` | `string` | e.g. `"Antalya"`; must be a `Region.Id` returned by `list_regions`, see [mcp-tools.md](./mcp-tools.md#list_regions) |
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
| `RegionId` | `string` (FK → `Region.Id`) | was a bare `string`; it is now a foreign key so `list_regions` has a catalog to read — see [Region catalog](#region-catalog) |
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

- `[DECIDED]`: keep `uuid` (v4) as PK, order and paginate by a `(created_at, id)` cursor **for jobs**. For **offers** the ordering column is `ScrapedAt` — `TourOffer` has no `CreatedAt` field, so any cursor over offers is `(scraped_at, id)`. The two cursors are not interchangeable; see [mcp-tools.md](./mcp-tools.md#pagination--mandatory).
- Alternative if chronological ids are genuinely needed: uuid7. `[FACT]` PostgreSQL **18** added a native `uuidv7()` (and `uuid_extract_timestamp()`); PostgreSQL 17 and earlier have neither, and the third-party `pg_uuidv7` extension is only needed below 18. `[OPEN]` — verify the deployed PostgreSQL version before adopting; the version is not pinned anywhere in these docs.

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
- `[DECIDED]`: there is **no** `idempotency_key` field — not in the `search_tours` input schema and not a column. Dedupe is server-derived from `ParametersHash`, so a client-supplied key would have nowhere to round-trip to. A caller-scoped key becomes meaningful only once caller identity exists (open question 2).
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
- `Cancelled` — the agent no longer wants the results. Reached directly from `Pending`, or from `InProgress` via `CancelRequestedAt` (the Worker releases the lease and writes the status).

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
| `PARAM_INVALID` | n/a | rejected before the job exists — tool-level only, never persisted; see [mcp-tools.md](./mcp-tools.md#errors--two-different-layers) |
| `JOB_NOT_FOUND` | n/a | unknown or foreign `job_id` — tool-level only, never persisted; see [mcp-tools.md](./mcp-tools.md#errors--two-different-layers) |
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
    public DateTime? CancelRequestedAt { get; set; }
    public List<string> Warnings { get; set; } = new();
    public int OffersCount { get; set; }
    public int PagesDone { get; set; }
    public int? PagesTotal { get; set; }
    public JobErrorCode? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool? Retryable { get; set; }

    // navigation only — reverse FK accessor, not a persisted column
    public List<TourOffer> Offers { get; set; } = new();
}

public enum JobStatus { Pending, InProgress, Completed, Failed, Cancelled }
```

## Region catalog

`list_regions` cannot be built from scraped data on demand and cannot be hardcoded twice (here and in the scraper). `[DECIDED]` — regions are a catalog entity:

| Entity | Field | Notes |
|---|---|---|
| `Region` | `Id` | `string` PK, slug — the value the agent passes back as `search_tours.region` |
| | `Label` | `string` display name |
| | `Source` | `string` source id; one source per milestone today, kept so a second source is a data change |
| | `Active` | `bool` — a region the source stopped selling is deactivated, not deleted |

- Seeded at startup from the same config the scraper reads (`selectors.json` region list) — one source of truth for region names.
- `TourOffer.RegionId` is a foreign key to it, so `Region` is a real entity rather than a bare string on the offer.
- `offers_count` in the tool output is **computed** from `tour_offers` through the `region` index — see [database.md](./database.md#index-rules). It is not a stored counter, so it cannot drift.
