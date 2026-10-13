# Scraping TourVisor.ru

Status legend: [agents.md](./agents.md#status-legend).

## Principles

- **Respect `robots.txt`** and the site's ToS. Check it at startup, not per request.
- **Throttling** — a concrete default, not `N`: **1 navigation per 4 seconds per domain**, max **1 browser per worker process** (see [runtime.md](./runtime.md#playwright-footprint)). Both are config-overridable.
- **Honest `User-Agent`** — the default and the only mode we ship today.
- **No scraping from CI or unit tests.** Only manual runs or integration tests with an explicit flag.

## The contradiction that had to be resolved

The previous version said "honest User-Agent" here and asked "do we need User-Agent rotation and a proxy pool?" in open questions. Those two are in direct conflict: an honest UA plus rotation plus proxies is a **evasion** strategy, and it is not a technical decision — it is a legal/compliance one.

`[DECIDED]`:

- Default mode: honest UA, obey `robots.txt`, obey throttling. No UA rotation, no proxy pool.
- If the source blocks us, the job goes `Failed` with `ANTI_BOT_DETECTED` and `Retryable = false` — we do **not** silently escalate.
- Evasion (UA rotation, proxy pool, CAPTCHA solving) is `[OPEN]` and **out of scope until there is explicit legal review and a written ADR**. It must not be implemented "because it seems necessary".

## Technology

- **Playwright for .NET** — headless Chromium. `[FACT]` official port `microsoft/playwright-dotnet`; requires the `playwright.ps1 install` step, see [runtime.md](./runtime.md#playwright-footprint).
- Alternative: `HttpClient` + `HtmlX`/`AngleSharp` — only if the page ships the data in static HTML. Verify before assuming it works.

## Selectors

- Kept in a separate **`selectors.json`**, not in `appsettings.json` (different change frequency, different reviewers).
- On markup change — edit the config, not the code.
- Keep "known good" HTML snapshots for parser regression tests.

`selectors.json` must have a schema and a version:

```json
{
  "schema_version": 3,
  "source": "tourvisor.ru",
  "updated_at": "2026-01-15",
  "pages": {
    "search_results": {
      "url_template": "https://tourvisor.ru/...?region={region_id}",
      "wait_for": { "selector": "[data-testid='results']", "timeout_ms": 15000 },
      "item": { "selector": "div.search-result", "min_items": 1 },
      "fields": {
        "hotel_name": { "selector": "h2.hotel-title", "required": true },
        "price":      { "selector": "span.price",     "required": true, "parse": "ru-price" },
        "meal_type":  { "selector": "span.meal",      "required": false }
      },
      "fallback_selectors": {
        "hotel_name": ["h2.hotel-title", "h2.title", "[data-testid='hotel-name']"]
      }
    }
  }
}
```

Rules:

- `schema_version` bumps on any selector change; the version is recorded in the job's `Warnings`/metadata so a regression can be attributed to a markup revision.
- A missing **required** field → `PAGE_STRUCTURE_CHANGED` (permanent).
- A missing **optional** field → a warning, `Degraded = true`.
- `fallback_selectors` are tried in order; the first hit wins and is logged.
- The file is validated at startup against a JSON Schema — a broken config must fail at startup, not at the first job.

## Partial results

`[DECIDED]`: if some pages parsed and some failed, the job is `Completed` with `Degraded = true` and `Warnings` listing what failed. Losing half the results is not the same as losing the job — an agent that gets 60 valid offers must not be told "nothing happened".

- A page failure is retried up to the per-page limit before it is counted as failed.
- `OffersCount` is the truth the agent sees; it never has to guess completeness.

## Retry and resilience

- **Polly** retry with exponential backoff — for **transient** errors only (`NETWORK_*`, `HTTP_5XX`, `BROWSER_CRASH`). Never retry `SELECTOR_NOT_FOUND`.
- Timeout per navigation and per job overall.
- `CancellationToken` is mandatory through every layer — see [conventions.md](./conventions.md).
- On failure — save an HTML snapshot.

## Snapshots

| Parameter | Value |
|---|---|
| Location | `TOURSEARCH_SNAPSHOT_DIR`, default `var/lib/toursearch/snapshots/` — **outside** the repo |
| Naming | `YYYY-MM-DD/<job_id>/<page>.html` |
| TTL | 7 days `[DECIDED]` default, configurable |
| Quota | hard size cap; oldest deleted first — an unbounded directory is a defect |
| Git | `**/snapshots/**` must be in `.gitignore` — scraped HTML contains third-party content |

## Anti-bot

- Detect explicitly: HTTP `429`, CAPTCHA markers, block pages.
- Map to `ANTI_BOT_DETECTED` (permanent) or `HTTP_429` (retryable, back off harder).
- Do not attempt to bypass. See the decision in [Principles](#the-contradiction-that-had-to-be-resolved).

## What we parse

Per offer: hotel name, star rating, region, check-in date, nights, price, room type (if present), meal type (if present), source URL. Normalized fields plus the untouched payload in `Raw` (see [domain.md](./domain.md)).

## Anti-patterns

- ❌ Hardcoded selectors in code.
- ❌ Scraping from unit tests.
- ❌ Ignoring `robots.txt`.
- ❌ Silent UA/proxy evasion without legal review.
- ❌ Storing HTML snapshots without TTL and a size cap.
- ❌ Treating a partial parse as a total failure.
