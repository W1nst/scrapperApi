# AGENTS.md — instructions for the agent that implements this application

This file is operational: it tells the coding agent **how to work in this repository**.
The specification itself lives in [docs/agents.md](./docs/agents.md).

---

## 0. Before you write a single line of code

Read in this order. Do not skim:

1. [docs/agents.md](./docs/agents.md) — entry point + status legend.
2. [docs/conventions.md](./docs/conventions.md) — **the source of truth for rules**. Everything below is a summary; if it contradicts conventions, conventions wins.
3. [docs/architecture.md](./docs/architecture.md) + [docs/runtime.md](./docs/runtime.md) — what must exist and what must not.
4. [docs/domain.md](./docs/domain.md) + [docs/database.md](./docs/database.md) — fields, statuses, error taxonomy, schema rules.
5. [docs/mcp-tools.md](./docs/mcp-tools.md) + [docs/scraping.md](./docs/scraping.md) — external contract.
6. [docs/open-questions.md](./docs/open-questions.md) — what is still undecided and which of those block you.

## 0.1 Who writes the code — delegation is mandatory

**Implementation code is written only by the `task-developer` subagent.** The main agent
specifies, decides, reviews and owns git; it does not type the implementation itself.

```
task-creator  →  tasks/queue/NNN-*.md   (specification)
task-solver   →  orchestrates: delegates the code, verifies behaviour, closes the task
task-developer → writes the code, proves it builds and the process starts  (never commits)
```

Rules:

- **Any task that changes more than a trivial amount of code must be delegated** to
  `task-developer` via the `subagent` tool, passing the task file path and the exact scope.
  "Trivial" means a one- or two-line edit in a single file; anything larger goes to the
  subagent.
- The subagent's responsibility stops at **compiles + starts**. It does not verify feature
  behaviour, does not certify acceptance criteria and does not decide open questions. The main
  agent verifies behaviour and accepts the result.
- The main agent must still read the diff it is about to accept. Delegation is not a substitute
  for review.
- One writer per working copy: never run two developer subagents against the same checkout.
- The model for this role is pinned in `~/.pi/agent/agents/task-developer.md` and can be changed
  there or overridden per run — see the precedence list in that file. Do not change it silently.

## 1. Repo state (verify, do not assume)

The repository is **no longer docs-only**. Two states coexist, and they are not the same:

| | Tracked by git (12 files) | Present but **untracked** |
|---|---|---|
| Contains | `.gitignore`, `AGENTS.md`, `README.md`, `docs/*.md` | `.editorconfig`, `Directory.Build.props`, `TourSearch.sln`, `src/` (6 projects), `tests/` (2 test projects + `fixtures/`), `tasks/` |
| Absent entirely | `docs/adr/`, CI config, a license, any real implementation code | — |

Run `git ls-files` **and** `git status` before you act — the tracked list alone no longer tells you what exists. A rule is `[PLANNED]` only if the artifact is missing from the working tree. An artifact that exists but is untracked is neither `[PLANNED]` nor established: do not cite it as a fact, and do not treat it as committed. **Never describe a plan as something that exists.**

The `Program.cs` files under `src/TourSearch.Api` and `src/TourSearch.Worker` are build-only placeholders that exit `1`; they exist so the skeleton compiles and **must not grow** until the gates in §2 are open. Verified state and the list of known dangling references live in [open-questions.md](./docs/open-questions.md#repo-state).

## 2. Order of work

Each step depends on the previous one. Do not reorder, do not parallelize across steps 1–3.

| # | Task | Gate |
|---|---|---|
| 1 | Write the ADRs for every `[DECIDED]` item already recorded in the docs | blocks all coding |
| 2 | Answer the **blocking** questions in [open-questions.md](./docs/open-questions.md#blocking) or state explicitly that you are using the proposed default | blocks the affected code |
| 3 | Root tooling: `.editorconfig`, `.slnx`/`.sln`, `TreatWarningsAsErrors`, `*.csproj` templates | **done but uncommitted** — verify with `git status` |
| 4 | `TourSearch.Core` — domain models, `ITourScraper`, status enums, error taxonomy | no I/O here |
| 5 | `TourSearch.Infrastructure` — the **single** `TourSearchDbContext` | must exist before any migration |
| 6 | `TourSearch.Infrastructure.Migration` — only `IDesignTimeDbContextFactory` + first migration | see §5 |
| 7 | `TourSearch.Worker` — job claim, lease, recovery pass | needs the DB schema |
| 8 | `TourSearch.Api` — MCP server + tools | needs the domain contract |
| 9 | `TourSearch.Scraper` — Playwright + `selectors.json` | needs fixtures first ([tests/fixtures/README.md](./tests/fixtures/README.md)) |
| 10 | Tests: unit (fixtures) + integration (Testcontainers PostgreSQL) | no test sources yet; `MSTEST0001` is suppressed as an interim measure in both test projects |

## 3. Hard rules — violations are not style issues

These are the rules whose violation produces a wrong system, not a cosmetic problem. Each has a link to the reasoning.

- **`Api` must not depend on `Scraper`.** No Playwright, no Chromium, no browser in the API process. [architecture](./docs/architecture.md#why-api-must-not-depend-on-scraper-decided)
- **One `TourSearchDbContext`.** It lives in `Infrastructure`. The Migration project references it through a design-time factory — it must not redeclare the model. Two `OnModelCreating()` definitions mean migrations generate tables the runtime does not expect. [architecture](./docs/architecture.md#why-migration-must-not-redeclare-dbcontext-decided)
- **Exactly one process starts `BackgroundService`** — the Worker. If the API starts one too, jobs get picked up twice. [runtime](./docs/runtime.md#startup)
- **Job claim is an atomic CAS** `Pending → InProgress` writing `lease_token`, `lease_expires_at`, `attempt + 1`. A killed worker must be recoverable. [runtime](./docs/runtime.md#restart-semantics)
- **Every persisted `DateTime` is UTC.** Npgsql + EF Core throws on a non-UTC value. [database](./docs/database.md#utc-trap)
- **A transient error must not permanently fail a job.** Use the taxonomy: `ErrorCode` + `Retryable`. [domain](./docs/domain.md#error-taxonomy)
- **Partial parse = `Completed` + `Degraded = true` + `Warnings`.** Not `Failed`. [scraping](./docs/scraping.md#partial-results)
- **`get_search_results` is paginated.** Never return an unbounded offer list into an agent context. [mcp-tools](./docs/mcp-tools.md#pagination--mandatory)
- **No secrets in committed files** — including fake-looking literals in examples. [conventions](./docs/conventions.md#configuration)
- **No test and no CI run may contact TourVisor.ru.** Parser tests use committed fixtures under `tests/fixtures/`. [conventions](./docs/conventions.md#testing)
- **No UA rotation, no proxy pool, no anti-bot evasion** without a legal review and an ADR. Polite mode is the default. [scraping](./docs/scraping.md#the-contradiction-that-had-to-be-resolved)
- **`Singleton` DI scope is for shared state** (DB pool, browser pool, `BackgroundService`). Default is `Scoped`. [conventions](./docs/conventions.md#di)
- **`CancellationToken` propagates through every layer.** Cancellation means "stop and release the lease", never "finish the job anyway". [conventions](./docs/conventions.md#cancellation)

## 4. Verification loop — run after every change

```bash
dotnet build TourSearch.sln
dotnet test TourSearch.sln --verbosity minimal
```

> `[FACT]` two spellings that used to be here are **not** real options in the .NET 10 SDK (10.0.401) and are rejected with `MSB1001`: `dotnet build --all-warnings-as-errors` and `dotnet test --summary=brief`. The warnings-as-errors guarantee comes from `TreatWarningsAsErrors` in `Directory.Build.props`, which applies to every project in the sln. Use the commands above verbatim; do not re-invent flags. The `TourSearch.sln` argument to `dotnet test` is required — without it the CLI picks up `bin/` output paths as projects (see §1).

Migrations only through the CLI, always with an explicit `--project`:

```bash
dotnet ef migrations add <Name> --project src/TourSearch.Infrastructure.Migration
dotnet ef migrations list       --project src/TourSearch.Infrastructure.Migration
dotnet ef database update       --project src/TourSearch.Infrastructure.Migration
```

Manual contract check for the MCP server (never against the live site):

```bash
npx @modelcontextprotocol/inspector   # connect to the local Streamable HTTP endpoint
```

A change is **not done** until build + tests pass and the affected docs are updated.

## 5. When you are uncertain

Stop and ask. Do not invent. Specifically:

- If two docs contradict each other — that is a defect in the spec. Report the contradiction, do not silently pick one side.
- If a decision contradicts an existing ADR — write a **new** ADR that supersedes it and get it reviewed before coding.
- If an external fact matters (library API, version, EOL date, MCP spec detail) — verify it against a source and mark it `[FACT]`. A half-remembered warning is `[UNVERIFIED]` until reproduced. [docs rule](./docs/conventions.md#documentation-rules)
- If you cannot verify a claim in these docs against the tooling, say so — do not copy it into code.

## 6. Git

- Conventional Commits: `feat:`, `fix:`, `chore:`, `docs:`, `adr:`.
- Branches `feature/*`, `fix/*`; PR required with at least one approval.
- One writer per working copy — do not run two agents against the same checkout.
- `.gitignore` already covers secrets, snapshots, Playwright binaries, build and test output. Do not add a tracked file that matches it.

## 7. Definition of done — per milestone

A milestone is done when all of these are true:

1. The code compiles with warnings as errors (`dotnet build TourSearch.sln`, §4).
2. Tests pass, including the parser fixtures, and no test touched the network. `[FACT]` both test projects are currently empty, so `dotnet test` exits `0` without running anything — an empty run does **not** satisfy this item. See [conventions.md](./docs/conventions.md#testing).
3. The DB state is reachable through the documented transitions only — no ad-hoc status writes.
4. The MCP contract in [mcp-tools.md](./docs/mcp-tools.md) matches the implementation **field for field**, including `outputSchema`.
5. Any decision you made on the way is written as an ADR and referenced from the docs.

## 8. Out of scope

Frontend, booking, payment, tourist PII, multi-source aggregation, and anti-bot evasion. If a task requires one of these, it is the wrong task — say so instead of implementing it.
