# Architecture Decision Records

One file per non-obvious decision, per [`docs/conventions.md#adrs`](../conventions.md#adrs).
Shape of every file: **Context**, **Decision**, **Consequences**, **Status**.

Ids are **3 digits** and are never reused or renumbered — they are referenced from the docs,
from `tests/*/*.csproj` comments, and from commit messages.

| ADR | Decision | Status | Stated in |
|---|---|---|---|
| [001](./001-api-does-not-depend-on-scraper.md) | `Api` does not depend on `Scraper` | Accepted | [architecture.md](../architecture.md#why-api-must-not-depend-on-scraper-decided) |
| [002](./002-migration-references-runtime-dbcontext.md) | Migration references the runtime `DbContext`; no second model | Accepted | [architecture.md](../architecture.md#why-migration-must-not-redeclare-dbcontext-decided) |
| [003](./003-no-secrets-in-committed-files.md) | No secrets in committed files, including examples | Accepted | [conventions.md](../conventions.md#configuration) |
| [004](./004-backgroundservice-started-by-worker-only.md) | `BackgroundService` is started by the Worker only | Accepted | [runtime.md](../runtime.md#startup) |
| [005](./005-job-claim-atomic-cas.md) | Job claim = atomic CAS + lease + recovery pass | Accepted | [database.md](../database.md#job-claim-mechanism) |
| [006](./006-partial-parse-is-completed-degraded.md) | Partial parse = `Completed` + `Degraded` | Accepted | [scraping.md](../scraping.md#partial-results) |
| [007](./007-error-taxonomy-retryable.md) | Error taxonomy carries `Retryable` | Accepted | [domain.md](../domain.md#error-taxonomy) |
| [008](./008-get-search-results-is-paginated.md) | `get_search_results` is paginated, never unbounded | Accepted | [mcp-tools.md](../mcp-tools.md#pagination--mandatory) |
| [009](./009-offer-cursor-is-rank-order.md) | Offer cursor is rank `(price, hotel_stars, id)`, not `(created_at, id)` | Accepted | [mcp-tools.md](../mcp-tools.md#pagination--mandatory) |
| [010](./010-list-regions-prevents-invented-regions.md) | `list_regions` exists so the agent cannot invent regions | Accepted | [mcp-tools.md](../mcp-tools.md) |
| [011](./011-regions-are-a-catalog-entity.md) | Regions are a catalog entity; `TourOffer.RegionId` is an FK | Accepted | [domain.md](../domain.md#region-catalog) |
| [012](./012-no-idempotency-key.md) | No `idempotency_key` — dedupe is server-derived | Accepted | [domain.md](../domain.md#identity-and-dedupe) |
| [013](./013-warnings-is-a-native-pg-array.md) | `Warnings` is a native PostgreSQL array column, not a child table | Accepted | [database.md](../database.md#warnings-column-representation) |
| [014](./014-honest-ua-no-evasion.md) | Honest UA, no evasion by default | Accepted | [scraping.md](../scraping.md) |
| [015](./015-singleton-is-for-shared-state.md) | `Singleton` is for shared state, not stateless helpers | Accepted | [conventions.md](../conventions.md#di) |
| [036](./036-test-parallelization-policy.md) | Test parallelization policy — **undecided** | **Proposed** | [conventions.md](../conventions.md#testing) |

Ids `016`–`035` are free. `036` is **reserved**, not "next": both test `.csproj` files already cite
`docs/adr/036-test-parallelization-policy.md`, so the number is fixed by existing text. Do not compact
the range.

## Adding an ADR

1. Take the next free id. Never reuse one.
2. Copy the four headings from any existing file: Context, Decision, Consequences, Status.
3. State what the docs already decided and link the section that decided it. Do not introduce a fact
   that is not in the docs.
4. A change that contradicts an existing ADR needs a **new** ADR that supersedes it — `status:
   Superseded by NNN` on the old one, and the new file references it. Never silently edit a decided ADR.
