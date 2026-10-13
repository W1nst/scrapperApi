# Open questions

Each question carries: **blocking?** (does it stop implementation), a proposed default, and what must be decided before which milestone.

Status legend: [agents.md](./agents.md#status-legend).

## Blocking

These change the shape of the code. They cannot be deferred to "later, while coding".

| # | Question | Proposed default | Why it blocks |
|---|---|---|---|
| 1 | **Polling vs push** — how does the agent learn results are ready? | Polling with `poll_after_ms`; optional SSE over Streamable HTTP for progress | Decides whether `get_search_results` is the only entry point or a notification channel exists |
| 2 | **Auth for the MCP server** — API key / OAuth / mTLS | API key in headers for a private deployment | Rate limiting, `list_recent_searches` ownership, and "per-caller" limits are all undefined without caller identity |
| 3 | **TTL** for `SearchJob`/`TourOffer` | 30 days jobs, offers follow via `Cascade`, snapshots 7 days | Decides whether offers can outlive a job, which decides `DeleteBehavior` |
| 4 | **Deployment** — Docker / Kubernetes / host | Single Docker image, two processes | Streamable HTTP needs a public HTTPS endpoint; the supervisor model (restart, grace period) is assumed everywhere in [runtime.md](./runtime.md) |
| 5 | **DB schema name** — `tour_search` or `public` | `tour_search` | Affects every migration; changing it later is a breaking change |

## Non-blocking (decide, but do not block on)

| # | Question | Proposed default |
|---|---|---|
| 6 | **Caching / reuse** — same params, reuse a job? | Reuse a `Completed` job within the TTL window via `parameters_hash`; `[OPEN]` reuse of `InProgress` |
| 7 | **Rate limiting** — jobs per hour per agent | Global cap now, per-caller once auth exists |
| 8 | **Anti-bot** — proxy pool, UA rotation | **Out of scope** until legal review + ADR, see [scraping.md](./scraping.md#the-contradiction-that-had-to-be-resolved) |
| 9 | **Monitoring** — Prometheus / OpenTelemetry | OpenTelemetry from the start; metrics: jobs by status, parse success/failure, scrape duration, browser RSS |
| 10 | **Secrets in production** — Vault / AWS Secrets Manager / plain env | Plain env vars for the first deployment; a secret manager is a later, explicit step |

## Already decided (must be written as ADRs before coding)

| Decision | Where |
|---|---|
| `Api` does not depend on `Scraper` | [architecture.md](./architecture.md) |
| Migration references the runtime `DbContext`, no second model | [architecture.md](./architecture.md) |
| No secrets in committed files, including examples | [conventions.md](./conventions.md) |
| `BackgroundService` is started by the Worker only | [runtime.md](./runtime.md) |
| Job claim = atomic CAS + lease + recovery pass | [domain.md](./domain.md), [runtime.md](./runtime.md) |
| Partial parse = `Completed` + `Degraded` | [scraping.md](./scraping.md) |
| Error taxonomy with `Retryable` | [domain.md](./domain.md) |
| `get_search_results` is paginated, never unbounded | [mcp-tools.md](./mcp-tools.md) |
| `list_regions` exists so the agent cannot invent regions | [mcp-tools.md](./mcp-tools.md) |
| Honest UA, no evasion by default | [scraping.md](./scraping.md) |
| `Singleton` is for shared state, not for stateless helpers | [conventions.md](./conventions.md) |

## Repo state

The repo currently contains **only `docs/`** — no sources, no `.gitignore`, no CI, no `.editorconfig`, no license, no `docs/adr/`. Every rule in these docs is `[PLANNED]` until the artifact exists.
