# ADR 001 — `Api` does not depend on `Scraper`

- **Status:** Accepted
- **Date:** 2026-10-13
- **Stated in:** [`docs/architecture.md#why-api-must-not-depend-on-scraper-decided`](../architecture.md#why-api-must-not-depend-on-scraper-decided)

## Context

An earlier version of the specification had `TourSearch.Api → TourSearch.Scraper`. That is a defect
rather than a stylistic choice:

- Playwright drags a headless Chromium into the API process — hundreds of MB of RSS and a browser
  binary on the hot path.
- The API must answer MCP calls in milliseconds; scraping takes minutes and is unstable.
- `Api` and `Worker` are deployed as separate processes. A Playwright dependency in `Api` means a
  broken browser kills the API, not just scraping.
- The API never needs to scrape. It needs scraping **state**, which it reads from `SearchJob.Status`.
- The API never needs to trigger scraping. It writes a job to the DB; the Worker picks it up.

## Decision

`TourSearch.Api` must not reference `TourSearch.Scraper`. No Playwright, no Chromium, no browser in
the API process.

Dependency direction, from [`docs/architecture.md`](../architecture.md):

| Project | May depend on | Must not depend on |
|---|---|---|
| `Api` | `Core`, `Infrastructure` | **`Scraper`** |
| `Worker` | `Core`, `Infrastructure`, `Scraper` | — |
| `Scraper` | `Core` | `Infrastructure` |
| `Infrastructure` | `Core` | `Api`, `Worker`, `Scraper` |

If a future requirement needs synchronous scraping in the API, it must be an explicit, separate,
opt-in service — not a transitive dependency.

## Consequences

- `Api` can answer `list_regions` and `get_search_results` with no browser installed at all.
- A crashing browser cannot take down the MCP endpoint; the failure surfaces as a job with
  `ErrorCode = BROWSER_CRASH` and `Retryable = true` (see [ADR 007](./007-error-taxonomy-retryable.md)).
- Deployment of `Api` does not need the `playwright.ps1 install` step or the Chromium binary, so the
  image is far smaller.
- Any code that both serves MCP and scrapes must be split across the two processes; that is the
  intended cost.
- Enforced by the build graph: `TourSearch.Api.csproj` carries no `Scraper` reference, and a comment
  in the file records the rule.
