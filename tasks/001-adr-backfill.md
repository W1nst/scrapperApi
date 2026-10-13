# 001 — Backfill `docs/adr/` with the `[DECIDED]` items

| | |
|---|---|
| **Status** | `ready` |
| **Type** | docs |
| **Created (UTC)** | `2026-10-13` |
| **Affected projects** | none — documentation only, no code changes |
| **Source request** | > «Создать docs/adr/ и записать все [DECIDED] решения как ADR» |

## Summary

Create the `docs/adr/` directory, write one ADR for each decision already marked `[DECIDED]` in the
specification, reserve the id already referenced from the test projects, add an index, and clear the
`[PLANNED]` / dangling-reference state that says this directory does not exist.

This is step 1 of [AGENTS.md §2](../../AGENTS.md) and **blocks all coding**: `docs/conventions.md#adrs`
requires every non-obvious decision to be recorded, and `AGENTS.md` §7.5 makes "a decision made on the
way is written as an ADR" part of the definition of done. The decisions are already made — this task
only writes them down.

## Problem

`docs/conventions.md#adrs` states: *"Every non-obvious decision gets a file `docs/adr/NNN-title.md`"*,
and the same section marks `docs/adr/` as `[PLANNED]` — it does not exist. Meanwhile:

- `docs/open-questions.md` lists **15** decisions under "Already decided (must be written as ADRs
  before coding)".
- `docs/database.md` carries a `[REQUIRED-ADR]` marker for the job-claim mechanism.
- `docs/agents.md` defines `[DECIDED]` as *"must be recorded as an ADR (`docs/adr/NNN-*.md`)"*.

So the specification currently requires an artifact that does not exist, and every `[DECIDED]` marker
in the docs points at nothing. Without this, no ADR exists to supersede, so the "a change that
contradicts an ADR requires a new ADR" rule is unenforceable — a future agent can change any decided
behaviour with nothing to contradict.

Two references are already **dangling** in committed sources: both test `.csproj` files cite
`docs/adr/036-test-parallelization-policy.md`, which does not exist.

## Scope

### In scope

- `docs/adr/` directory, one file per decision in the "Already decided" table of
  [open-questions.md](../../docs/open-questions.md).
- `docs/adr/036-test-parallelization-policy.md` — created to resolve the dangling csproj references.
- `docs/adr/README.md` — index of all ADRs with status and the decision each records.
- Back-links from the docs that state a decision to the ADR that records it.
- Correcting the `[PLANNED]` markers and the "does not exist yet" claims that this task makes false.

### Out of scope

- **Deciding anything new.** This task records decisions already marked `[DECIDED]`. Any decision not
  already in the docs must not be invented here — see *The 036 rule* below.
- Implementing any of the decisions. `docs/adr/` records intent; the code comes later.
- The 5 **blocking** and 6 non-blocking open questions in
  [open-questions.md#blocking](../../docs/open-questions.md#blocking). They stay open. Recording a
  proposed default as if it were a decision is a defect.
- Reformulating the decisions. The ADR restates what the docs already decided, with a rationale and
  consequences drawn from the doc text.
- Frontend, booking, payment, tourist PII, multi-source aggregation, anti-bot evasion — out per
  `AGENTS.md` §8, and nothing here wants them.

## Requirements

### Functional

**FR-001 — `docs/adr/` exists and is indexed**

- `docs/adr/` exists and contains `README.md`.
- `README.md` lists every ADR file with its id, title, status, and a one-line statement of the
  decision it records.
- The index links resolve (relative links, existing targets).

*Acceptance:* every `*.md` in `docs/adr/` except `README.md` appears as a link in `README.md`, and the
count matches.

**FR-002 — one ADR per decision in the "Already decided" table**

- Each of the 15 rows in
  [open-questions.md](../../docs/open-questions.md) "Already decided" maps to exactly one ADR file.
- Each ADR states which decision it records and links to the doc section that made the decision.
- No ADR records more than one of the 15 decisions, and no decision is missing.

*Acceptance:* a 15-row traceability table in the outcome (decision → ADR path) with no gaps and no
duplicates.

**FR-003 — every ADR follows the documented shape**

- Each file has: **context**, **decision**, **consequences**, **status** — the four fields named in
  `docs/conventions.md#adrs`.
- `status` is `Accepted` for the 15 backfilled decisions — they are already marked `[DECIDED]` in the
  docs, so the decision is made and the ADR records it. `Proposed` would add a second gate the
  specification does not have.
- An **Alternatives considered** section is present where the docs already record a rejected
  alternative (e.g. `Api`↛`Scraper`; the CAS claim vs read-then-write; `uuid` vs `uuidv7`;
  `Warnings` array vs child table). Where the docs record no alternative, the ADR says so rather than
  inventing one.

*Acceptance:* every ADR file contains the four required headings; grep confirms.

**FR-004 — id `036` is reserved and the dangling references resolve**

- `docs/adr/036-test-parallelization-policy.md` exists.
- It records the **question** (MSTest 4.0.2 requires an explicit parallelization policy; the spec is
  silent) and the two options (`[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]` vs
  `[assembly: DoNotParallelize]`), for the unit and integration projects separately — the integration
  projects share one Testcontainers PostgreSQL instance, so the choice there is not the same.
- Its `status` is **`Proposed`**, and it explicitly states that the policy is **not decided**. This is
  the one file that must not invent a decision: see *The 036 rule*.
- The `INTERIM` comments in `tests/TourSearch.UnitTests/TourSearch.UnitTests.csproj` and
  `tests/TourSearch.IntegrationTests/TourSearch.IntegrationTests.csproj` now point at an existing file.

*Acceptance:* the file exists; both csproj paths resolve; the file's `status` is `Proposed`.

**FR-005 — the docs no longer claim `docs/adr/` is absent**

- `docs/conventions.md#adrs` — the `[PLANNED]` marker on the directory is replaced with the fact that
  it exists, and the "does not exist yet" sentence is removed.
- Any other place asserting the directory is missing is corrected the same way.
- The `[FACT]` note about 3-digit numbering stays — it is now explained by the real files.

*Acceptance:* grep for the stale claims returns nothing, except where the text intentionally quotes
the old state.

**FR-006 — decisions are traceable in both directions**

- Each of the 15 decisions in `open-questions.md` links to its ADR.
- Each ADR links back to the doc section that made the decision.
- A reader arriving from either direction reaches the other.

*Acceptance:* spot-check 5 decisions in each direction; all resolve.

**FR-007 — `open-questions.md` reflects the new reality**

- The "Already decided (must be written as ADRs before coding)" table gains a third column with the
  ADR path, or is converted to link each row to its ADR.
- The heading's promise ("must be written as ADRs before coding") is retained but no longer describes
  pending work once the files exist.

*Acceptance:* every row in that table contains a working link to an ADR file.

### Non-functional

**NFR-001 — no new claims**

No ADR may introduce a fact, version, or external claim that is not already in the docs. If a decision
needs an external fact (e.g. MSTest behaviour, EF Core array mapping), cite the source already cited in
the docs and keep its `[FACT]` / `[UNVERIFIED]` marker — do not upgrade an unverified claim to verified.

**NFR-002 — consistency, not volume**

The 15 ADRs restate existing decisions. A file longer than ~60 lines is a signal that it is inventing
detail rather than recording a decision.

## Technical design

### Affected projects and dependency direction

| Project | Change | Does it break `Api` ↛ `Scraper`? |
|---|---|---|
| — | none — docs only | no |

No `.cs` file, no `.csproj`, no migration is touched. `Directory.Build.props` and `TourSearch.sln` are
untouched.

### Data and database impact

None. No entity, migration, index, or TTL change. No `DateTime` is persisted.

### MCP contract impact (`docs/mcp-tools.md`)

`Не применимо — контракт MCP не меняется.` The ADRs *describe* existing contract decisions (three
tools, no `idempotency_key`, paginated `get_search_results`, the offer cursor) but the contract file's
content is not modified beyond adding ADR cross-links where a decision is stated.

### Runtime impact (`docs/runtime.md`)

`Не применимо` — no process, lease, exit code, or configuration change.

### DI and cancellation

`Не применимо` — no code.

### External dependencies

None added. One ADR cites MSTest 4.0.2 behaviour; that version is already pinned in
`tests/*/*.csproj` and is recorded as `[FACT]` in those files' comments.

### File plan

```
docs/adr/
├── README.md                                  # index
├── 001-api-does-not-depend-on-scraper.md
├── 002-migration-references-runtime-dbcontext.md
├── 003-no-secrets-in-committed-files.md
├── 004-backgroundservice-started-by-worker-only.md
├── 005-job-claim-atomic-cas.md                 # DB marker calls it `NNN-job-claim-cas`
├── 006-partial-parse-is-completed-degraded.md
├── 007-error-taxonomy-retryable.md
├── 008-get-search-results-is-paginated.md
├── 009-offer-cursor-is-rank-order.md
├── 010-list-regions-prevents-invented-regions.md
├── 011-regions-are-a-catalog-entity.md
├── 012-no-idempotency-key.md
├── 013-warnings-is-a-native-pg-array.md
├── 014-honest-ua-no-evasion.md
├── 015-singleton-is-for-shared-state.md
└── 036-test-parallelization-policy.md          # Proposed — reserves the referenced id
```

Numbering: `001`–`015` are the backfill in the order of the "Already decided" table, so the table
reads top-to-bottom as the index. `036` is **reserved**, not "next": both test `.csproj` files already
cite `036`, so the id is fixed by existing text. Ids `016`–`035` stay free for future ADRs — do not
compact the range, because `036` must remain meaningful.

### The 036 rule

`036` is the only file that must **not** decide anything. The test projects suppress `MSTEST0001` as an
interim measure, and their own comments say the durable fix is an assembly attribute whose policy the
spec does not state. Deciding that policy inside a documentation-backfill task would be exactly the
"invent a decision while doing something else" failure that `AGENTS.md` §5 forbids.

So: record the question, the two options, the fact that the integration project has a different
constraint (shared Testcontainers instance → parallelization is a flakiness hazard), and set
`status: Proposed`. Leave the choice to its own task.

## Hard rules (AGENTS.md §3)

| Rule | Applies | How the implementation respects it |
|---|---|---|
| `Api` must not depend on `Scraper` | no — docs only | ADR 001 records the rule; no project file changes |
| One `TourSearchDbContext` in `Infrastructure` | no — docs only | ADR 002 records the rule; Migration project untouched |
| Exactly one process starts `BackgroundService` (Worker) | no — docs only | ADR 004 records the rule |
| Job claim = atomic CAS (`Pending → InProgress`, lease + attempt+1) | no — docs only | ADR 005 records the rule; satisfies the `[REQUIRED-ADR]` marker in database.md |
| Every persisted `DateTime` is UTC | no | no code, nothing persisted |
| Transient error must not permanently fail a job | no — docs only | ADR 007 records the taxonomy |
| Partial parse = `Completed` + `Degraded = true` + `Warnings` | no — docs only | ADR 006 records the rule; ADR 013 records the column representation |
| `get_search_results` is paginated | no — docs only | ADR 008 records the rule |
| No secrets in committed files, including examples | **yes** | ADR 003 records the rule; no credential, token, connection string, or fake-looking literal appears in any ADR or in this task file |
| No test/CI contacts TourVisor.ru; fixtures under `tests/fixtures/` | **yes** | no test is written or run; no ADR prescribes a live call. ADR 014 records the honest-UA decision |
| No UA rotation / proxy pool / anti-bot evasion without legal review + ADR | no — docs only | ADR 014 records the default (no evasion) |
| `Singleton` only for shared state; default `Scoped` | no — docs only | ADR 015 records the rule |
| `CancellationToken` propagates through every layer | no | no code |

## Edge cases and error handling

| Case | Expected behaviour | `ErrorCode` | `Retryable` |
|---|---|---|---|
| A decision in the table has no clear "context" in the docs | ADR states the context as recorded in the doc and links it; do not invent motivation | n/a | n/a |
| Two docs state the same decision differently | that is a spec defect — report it, do not silently pick one (`AGENTS.md` §5) | n/a | n/a |
| A decision already has a rejected alternative recorded | include it under "Alternatives considered", citing the doc | n/a | n/a |
| A `[DECIDED]` marker exists outside the 15-row table (there are ~20 more, e.g. TTL defaults, `CurrencyCode`, `MaxBrowsers = 1`) | **not** part of this task's 15. Record them only if the id is free and the decision is unambiguous; otherwise list them in the outcome as candidates, and do not let the 15-row acceptance criterion drift | n/a | n/a |
| `docs/adr/` already exists when the task starts | stop and report — the task's premise is wrong; re-scope with `task-creator` | n/a | n/a |
| `036` already exists with a different meaning | stop — the id is claimed by the csproj references; resolve before continuing | n/a | n/a |
| An ADR would need a decision not in the docs | do not write it; report the gap | n/a | n/a |

Cancellation: not applicable — no long-running operation. If the task is abandoned mid-way, the ADR
directory may be partially populated; that is safe, because `README.md` is written last and the
outcome records how many files were produced.

## State transitions

`Не применимо` — no `SearchJob` status is touched, and no DB write of any kind occurs.

## Docs to update

| File | What changes | Required by |
|---|---|---|
| `docs/adr/*.md` | **created** — 16 files + `README.md` | `docs/conventions.md#adrs`, `AGENTS.md` §2 step 1 |
| `docs/conventions.md` | `#adrs`: drop the `[PLANNED]` marker and the "does not exist yet" sentence; keep the 3-digit `[FACT]` note | FR-005 |
| `docs/open-questions.md` | "Already decided" table: link each row to its ADR | FR-007 |
| `docs/database.md` | `[REQUIRED-ADR]` at the job-claim section: point at `docs/adr/005-job-claim-atomic-cas.md` instead of `NNN-job-claim-cas.md` | FR-006 |
| `AGENTS.md` | §2 step 1 gate: mark the ADR requirement satisfied (the docs are now written), keeping the step itself | FR-005 |
| `tests/*/*.csproj` | `INTERIM` comments now cite an existing file — the wording stays, since the policy is still undecided; only fix the reference if it is wrong | FR-004 |
| `docs/agents.md` | only if the `[DECIDED]` legend wording needs to point at a real file | FR-006 |

## ADRs required

| ADR | Action | Context |
|---|---|---|
| `docs/adr/001`–`015` | create | the 15 rows of the "Already decided" table in [open-questions.md](../../docs/open-questions.md) |
| `docs/adr/036-test-parallelization-policy.md` | create with `status: Proposed` — records an **open** question, decides nothing | dangling references in both test csproj files |

**This task is the bootstrap of the ADR process**: it cannot follow a requirement that ADRs exist, since
creating them is its own purpose. The decisions it records are already marked `[DECIDED]` in the docs,
so no new decision is introduced.

## Verification

```bash
dotnet build TourSearch.sln
dotnet test TourSearch.sln --verbosity minimal
```

> `[FACT]` `--all-warnings-as-errors` and `--summary=brief` are **not** real .NET 10 SDK options
> (`MSB1001`). Warnings-as-errors comes from `TreatWarningsAsErrors` in `Directory.Build.props`.
> Use the commands verbatim.

These two commands prove only that the documentation work broke nothing. They are **not** evidence for
any functional criterion — no code changes in this task. `dotnet test` will report "no tests available"
and exit `0`; that is expected and proves nothing.

Task-specific proof:

- `ls docs/adr/*.md` — 17 files (16 ADRs + `README.md`).
- For each of the 15 rows in the "Already decided" table: a link resolves to an existing file.
- Every ADR contains the four required headings (`grep -c` over context/decision/consequences/status).
- `docs/adr/036-test-parallelization-policy.md` exists and contains `status: Proposed`.
- `grep -rn "does not exist yet" docs/conventions.md` → no hit for `docs/adr/`.
- Link check: every relative link in `docs/adr/*.md` resolves.
- `grep -rn "docs/adr/NNN" docs/ AGENTS.md` → the only remaining hit is intentional template text.

No migration is involved:

```bash
# not applicable — no schema change
```

MCP contract check: `Не применимо` — the contract is not modified. No `npx @modelcontextprotocol/inspector`
run is needed, and none must be run against the live site.

## Definition of done

1. `dotnet build TourSearch.sln` passes with 0 warnings (nothing broken by the docs work).
2. Tests pass as before. `[FACT]` both test projects are empty, so `dotnet test` exits `0` without
   running anything — this item is satisfied trivially and proves nothing; say so rather than claiming
   coverage.
3. No DB state is involved — no ad-hoc status writes, no transitions.
4. `docs/mcp-tools.md` is unchanged, so its contract still matches the implementation field for field.
5. **This task's own subject**: 15 decision-ADRs plus the reserved `036` exist, are indexed, and are
   linked from the docs that state the decisions. Any decision made on the way — including "036 stays
   Proposed" — is recorded in an ADR and referenced from the docs.

## Repo drift found

Recorded while preparing this task; verify before starting, and correct anything still stale.

| Claim | Where | Reality |
|---|---|---|
| `docs/adr/` does not exist yet | `docs/conventions.md#adrs` | correct at authoring time — this task creates it |
| `docs/adr/036-test-parallelization-policy.md` | both `tests/*/*.csproj` `INTERIM` comments | file absent — dangling reference, resolved by FR-004 |
| "open question 12" in `docs/open-questions.md` | both `tests/*/*.csproj` comments | no question 12 exists — the file has 11; numbering must be extended when the policy is decided |
| `docs/adr/NNN-job-claim-cas.md` | `docs/database.md` job-claim `[REQUIRED-ADR]` | placeholder path; resolved by FR-006 |
| numbering scheme | `docs/conventions.md#adrs` previously implied 2 digits | `[FACT]` it is 3 digits, fixed by the `036` references. Already corrected in the docs; the ADRs must follow it |
| "repository is docs-only" | `AGENTS.md` §1, `README.md` | already corrected — the tree contains `src/`, `tests/`, root tooling, all untracked |

## Open questions

- **Scope of the backfill.** This task records the 15 rows of the "Already decided" table. The docs
  contain roughly 20 further `[DECIDED]` markers (TTL defaults, `CurrencyCode = RUB`,
  `MaxBrowsers = 1`, `SearchJob` TTL, snapshot TTL, and others). → proposed default: **out of scope**;
  list them in the outcome as candidates for a follow-up task → blocks: nothing. If the reviewer wants
  them included, the acceptance criterion for FR-002 changes from 15 to ~35 and the task should be
  split.
- **`036` id gap** (`016`–`035` unused). → proposed default: leave the gap; do not renumber → blocks:
  nothing. Renumbering would break the existing csproj references.
- **Whether `AGENTS.md` §2 step 1 should be struck once done.** → proposed default: keep the step but
  mark it satisfied, so the ordering of work survives the backfill → blocks: nothing.
