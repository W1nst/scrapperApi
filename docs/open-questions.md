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
| 11 | **PostgreSQL version** — what is deployed | Not pinned anywhere; pin it before any `uuidv7()` / version-gated feature claim (see [database.md](./database.md#uuid-vs-uuid7)) |
| 12 | **Test parallelization policy** — `[assembly: Parallelize]` vs `[assembly: DoNotParallelize]`, and whether the unit and integration projects answer differently | Not decided. Recorded in [ADR 036](./adr/036-test-parallelization-policy.md) as `Proposed`; must be resolved when the first real test file is added. The integration project shares one Testcontainers PostgreSQL instance, so parallelizing it is a flakiness hazard |

## Already decided (written up as ADRs — see [docs/adr/](./adr/README.md))

All 15 decisions are recorded. This table is kept as the traceability index: decision → where it is
stated → the ADR that fixes it. Changing one of these requires a **new** ADR that supersedes it, not an
edit to the doc.

| Decision | Stated in | ADR |
|---|---|---|
| `Api` does not depend on `Scraper` | [architecture.md](./architecture.md) | [001](./adr/001-api-does-not-depend-on-scraper.md) |
| Migration references the runtime `DbContext`, no second model | [architecture.md](./architecture.md) | [002](./adr/002-migration-references-runtime-dbcontext.md) |
| No secrets in committed files, including examples | [conventions.md](./conventions.md) | [003](./adr/003-no-secrets-in-committed-files.md) |
| `BackgroundService` is started by the Worker only | [runtime.md](./runtime.md) | [004](./adr/004-backgroundservice-started-by-worker-only.md) |
| Job claim = atomic CAS + lease + recovery pass | [domain.md](./domain.md), [runtime.md](./runtime.md), mechanism in [database.md](./database.md#job-claim-mechanism) | [005](./adr/005-job-claim-atomic-cas.md) |
| Partial parse = `Completed` + `Degraded` | [scraping.md](./scraping.md) | [006](./adr/006-partial-parse-is-completed-degraded.md) |
| Error taxonomy with `Retryable` | [domain.md](./domain.md) | [007](./adr/007-error-taxonomy-retryable.md) |
| `get_search_results` is paginated, never unbounded | [mcp-tools.md](./mcp-tools.md) | [008](./adr/008-get-search-results-is-paginated.md) |
| Offer cursor = rank `(price, hotel_stars, id)`, not `(created_at, id)` | [mcp-tools.md](./mcp-tools.md#pagination--mandatory) | [009](./adr/009-offer-cursor-is-rank-order.md) |
| `list_regions` exists so the agent cannot invent regions | [mcp-tools.md](./mcp-tools.md) | [010](./adr/010-list-regions-prevents-invented-regions.md) |
| Regions are a catalog entity, `TourOffer.RegionId` is an FK | [domain.md](./domain.md#region-catalog) | [011](./adr/011-regions-are-a-catalog-entity.md) |
| No `idempotency_key` — dedupe is server-derived | [domain.md](./domain.md#identity-and-dedupe) | [012](./adr/012-no-idempotency-key.md) |
| `Warnings` is a native PG array column, not a child table | [database.md](./database.md#warnings-column-representation) | [013](./adr/013-warnings-is-a-native-pg-array.md) |
| Honest UA, no evasion by default | [scraping.md](./scraping.md) | [014](./adr/014-honest-ua-no-evasion.md) |
| `Singleton` is for shared state, not for stateless helpers | [conventions.md](./conventions.md) | [015](./adr/015-singleton-is-for-shared-state.md) |

`[OPEN]` — the test parallelization policy is **not** one of the above: it is recorded as
[ADR 036](./adr/036-test-parallelization-policy.md) with `status: Proposed`, because the specification
does not decide it. See question 12 below.

## Repo state

`[FACT]` verified with `git ls-files`, `git status` and `find` on 2026-10-13.

**Tracked** — 12 files, 2 commits:

```
.gitignore  AGENTS.md  README.md  docs/*.md (10 files)
```

**Present in the working tree but NOT tracked** (all of it is new, uncommitted work):

| Path | What it is |
|---|---|
| `.editorconfig` | root style config |
| `Directory.Build.props` | `net10.0`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors` |
| `TourSearch.sln` | solution over all 8 projects |
| `src/TourSearch.Api` | placeholder `Program.cs` + csproj (MCP, no `Scraper` reference) |
| `src/TourSearch.Core` | csproj only — no sources |
| `src/TourSearch.Infrastructure` | csproj only — no sources |
| `src/TourSearch.Infrastructure.Migration` | csproj only — no sources |
| `src/TourSearch.Scraper` | csproj only — no sources |
| `src/TourSearch.Worker` | placeholder `Program.cs` + csproj |
| `tests/TourSearch.UnitTests` | csproj only — no tests |
| `tests/TourSearch.IntegrationTests` | csproj only — no tests |
| `tests/fixtures/README.md` | fixtures policy — no fixtures yet |
| `tasks/` | task queue, produced by the `task-creator` skill |

**Absent entirely**: `docs/adr/`, CI config, a license, any real implementation code.

The two `Program.cs` files are intentionally non-functional placeholders that exit `1`; they exist only so the skeleton builds. They must not grow — see the comments in those files.

### Consequences for how you read these docs

- A rule describing `.editorconfig`, the `.sln`, project structure, or package pinning is **no longer `[PLANNED]` as an artifact** — the file exists. But since none of it is committed yet, **do not cite it as established**: verify with `git ls-files` first.
- A rule about `docs/adr/`, CI, or a license is still `[PLANNED]` — check the tree before repeating that claim.
- `[FACT]` `AGENTS.md §1` and `README.md` previously claimed the repo was "docs only". That was true at the first commit and is now false. The claim has been corrected; if you find another copy of it, it is stale — fix it rather than trusting it.
- `[UNVERIFIED]` whether the untracked work builds from a clean clone: no `git ls-files` entry means `Directory.Build.props` and the projects are not covered by any tracked commit. Treat a clean-clone build as unproven until this state is committed.

### Known dangling references

`[FACT]` these point at artifacts that do not exist and must be resolved in step 1 (ADRs) or step 10 (tests):

| Reference | Points at | Status |
|---|---|---|
| `docs/database.md` job-claim section | `docs/adr/NNN-job-claim-cas.md` placeholder path | **resolved** — now points at [adr/005-job-claim-atomic-cas.md](./adr/005-job-claim-atomic-cas.md) |
| both test csproj comments | "open question 12" in this file | **resolved** — question 12 now exists (test parallelization policy) |
| both test csproj comments | `docs/adr/036-test-parallelization-policy.md` | **resolved** — file created with `status: Proposed` |

Scratch helpers (`.tmp-*.js`) must stay gitignored — see the `.tmp-*` pattern in [.gitignore](../.gitignore).
