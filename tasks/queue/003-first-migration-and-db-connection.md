# 003 — The first migration and the DB connection

| | |
|---|---|
| **Status** | `ready` |
| **Type** | infra |
| **Created (UTC)** | `2026-10-15` |
| **Affected projects** | `TourSearch.Infrastructure.Migration`, `TourSearch.Infrastructure`, `tests/TourSearch.UnitTests` |
| **Source request** | > «создать миграции и сделать подключение к бд» |

## Summary

Create the first EF Core migration for the schema defined in task `002`, wire the design-time factory
that `dotnet ef` needs, add `AddInfrastructure()` so both the design-time and runtime graphs build the
context identically, and prove the schema actually applies to a real PostgreSQL 17 server.

This is `AGENTS.md` §2 step 6, with the `AddInfrastructure()` part of step 8 pulled in deliberately:
without it the design-time factory would have to duplicate the options chain, which is the exact
duplication ADR 002 exists to prevent.

## Problem

The model exists (task `002`) but nothing can turn it into tables:

- `src/TourSearch.Infrastructure.Migration/` contains **only a `.csproj`** — no
  `IDesignTimeDbContextFactory`, so `dotnet ef migrations add` has nothing to build a context from.
- `TourSearch.Infrastructure` exposes `TourSearchDbContext` but has **no** `AddInfrastructure()` and no
  connection wiring, so neither the API nor the Worker can obtain a context yet.
- `snake_case` **depends on the caller chaining `.UseSnakeCaseNamingConvention()`** (verified in task
  `002`; it is an extension on `DbContextOptionsBuilder`, not `ModelBuilder`). If the factory and the
  runtime graph chain it differently — or one forgets — columns silently become `PascalCase` and
  migrations stop matching the runtime model. This is a real, live risk left open by task `002`'s
  `## Outcome`.

## Scope

### In scope

- `IDesignTimeDbContextFactory<TourSearchDbContext>` in `TourSearch.Infrastructure.Migration`.
- `AddInfrastructure()` in `TourSearch.Infrastructure`: `NpgsqlDataSource` (`Singleton`) +
  `DbContext` (`Scoped`), with the snake_case plugin chained in **one** shared place.
- The **first migration** (`InitialSchema` or equivalent) covering all three tables and the eight indexes
  from task `002`.
- Applying the migration to the local PostgreSQL 17 database `tour_search` and verifying the resulting
  schema against the model.
- A unit test that pins the migration/model agreement where it can be checked without a server.
- Correcting any `[OPEN]`/unverified claim in `docs/architecture.md` about the SQL-export flags, using a
  fact checked against the installed CLI.

### Out of scope

- **The runtime startup path** (`Program.cs` for Api/Worker, `RunApplication()`). `AGENTS.md` §2 steps 7
  and 8 own it. This task provides `AddInfrastructure()`; it does not call it from a process.
- **Region seeding.** `docs/domain.md` seeds regions from `selectors.json` at startup, and that file does
  not exist yet (step 9). Seeding an empty catalog is not possible and not wanted here.
- **Integration tests with Testcontainers.** The Docker **daemon is not running** on this machine (only
  the CLI is installed — verified). A test that cannot run is worse than no test; this becomes its own
  task when the daemon is available. Note this also means the integration project's `MSTEST0001`
  suppression stays (open question 12).
- **Deployment** (Docker/K8s/host) — blocking open question 4, explicitly deferred by the user. Nothing
  in this task presumes a deployment model; it talks to a local server only.
- **TTL cleanup job**, repository classes, `ITourScraper`, anything Playwright.
- Frontend, booking, payment, tourist PII, multi-source aggregation, anti-bot evasion — out per
  `AGENTS.md` §8.

## Requirements

### Functional

**FR-001 — the design-time factory exists and builds the same model as the runtime**

- `TourSearch.Infrastructure.Migration` contains exactly one
  `IDesignTimeDbContextFactory<TourSearchDbContext>` implementation.
- It reads connection settings from **environment variables** (`TOURSEARCH_DB_*`), because — per
  `docs/runtime.md` — `dotnet ef` runs the factory in a **separate process** that does not see the app's
  DI graph or secret provider.
- It chains `UseNpgsql(...)` **and** `.UseSnakeCaseNamingConvention()`, so the design-time model is
  identical to the runtime one.
- It does **not** redeclare `TourSearchDbContext` and contains **no** `OnModelCreating`. Exactly one
  `DbContext` type must remain in the whole solution.

*Acceptance:* `dotnet ef migrations list --project src/TourSearch.Infrastructure.Migration` runs and does
not error; a test asserts exactly one `DbContext` type across loaded assemblies.

**FR-002 — `AddInfrastructure()` wires the context once, correctly**

- `AddInfrastructure(this IServiceCollection)` exists in `TourSearch.Infrastructure`.
- It registers `NpgsqlDataSource` as **`Singleton`** (`docs/database.md#connection-pool` recommends
  `NpgsqlDataSource`; it is shared state, so `Singleton` per ADR 015) and the `DbContext` as **`Scoped`**
  (`docs/conventions.md#di` — `Scoped` is the default).
- It applies `.UseSnakeCaseNamingConvention()` in **one** place, shared with the design-time path, so the
  two cannot drift.
- It reads configuration from the `TOURSEARCH_DB_*` environment variables and **fails fast** on a missing
  required value, rather than starting with a broken context (`docs/runtime.md#startup`).
- It does **not** register a `BackgroundService` (ADR 004) and does **not** call
  `EnsureCreated()`/`Migrate()` at startup.

*Acceptance:* the registration compiles; a test resolves the service graph and asserts the lifetimes and
that `SearchJob` is reachable through the resolved context.

**FR-003 — the first migration exists and matches the model**

- Exactly one migration in `TourSearch.Infrastructure.Migration/Migrations/`, named by meaning
  (e.g. `InitialSchema`) — never `Migration1`.
- Its `Up` creates, in the `tour_search` schema: `regions`, `search_jobs`, `tour_offers`, with the
  columns and types from task `002` — notably `parameters jsonb`, `warnings text[]`,
  `currency_code varchar(3)` (not `char(3)`), `price numeric(12,2)`, all timestamps
  `timestamp with time zone`.
- It creates all eight indexes, with `(status, created_at)` and `(scraped_at, id)` in that column order.
- It creates both FKs with the correct delete behaviour: `search_jobs → Cascade`,
  `regions → Restrict`.
- No column, index, constraint or table that the model does not define.
- The migration is **generated**, not hand-written. If any line is edited by hand, the reason is stated
  in the migration body (`docs/database.md#migrations`).

*Acceptance:* the generated SQL contains every item above; a test asserts that the model has **no**
pending differences from the migration snapshot (`context.Database.GetPendingMigrations()` is empty and
the model snapshot is in sync).

**FR-004 — the schema applies to a real server and is verified there**

- `dotnet ef database update --project src/TourSearch.Infrastructure.Migration` applies cleanly to
  **`tour_search` on the local PostgreSQL 17.4**, using credentials supplied through the environment.
- The resulting schema is inspected **on the server** (`information_schema` / `pg_indexes`), not just
  read from the generated script, and compared against the model.
- `dotnet ef migrations list` reports the migration as **applied**.

*Acceptance — the specific checks:* on the server, `tour_search` contains the three tables; `search_jobs`
has `parameters jsonb` and `warnings text[]`; `tour_offers` has `currency_code character varying(3)`;
the FK on `region_id` shows `ON DELETE RESTRICT` and the one on `search_job_id` shows `ON DELETE CASCADE`;
all eight indexes exist with their column order; every timestamp column is `timestamp with time zone`.

**FR-005 — re-running is idempotent and a second migration is a no-op**

- Running `database update` twice succeeds and changes nothing the second time.
- `dotnet ef migrations has-pending-model-changes --project src/TourSearch.Infrastructure.Migration`
  (or the equivalent for the installed CLI) reports **no** pending model changes after the migration
  exists. If that command is unavailable in EF 10, say so and use the model-snapshot test instead.

*Acceptance:* the commands are run and their real output is quoted.

**FR-006 — the connection settings are documented, and no secret is committed**

- The `TOURSEARCH_DB_*` variable set is documented consistently in `docs/runtime.md` and
  `docs/architecture.md`, including the two names that are currently missing from the docs —
  `TOURSEARCH_DB_PORT` and `TOURSEARCH_DB_NAME` — without which a connection string cannot be built.
- **No credential appears in any committed file**, including examples: not a password, not a connection
  string with an inline value, and not a fake-looking literal. `docs/conventions.md#configuration` and
  [ADR 003](../../docs/adr/003-no-secrets-in-committed-files.md) both forbid this, and `tasks/` is a
  tracked, published directory.
- `appsettings.json` may carry host/port/database defaults only, exactly as
  `docs/architecture.md#configuration-and-secrets` already shows — **no secret fields**.

*Acceptance:* a grep over the diff for password-like literals returns nothing; the documented variable
names match those the code reads.

**FR-007 — the SQL-export question in the docs is resolved with a checked fact**

- `docs/architecture.md` currently marks the export flags as `[OPEN]` — "the exact flag set
  (`migrations export` / `generate`) must be verified against `dotnet ef migrations --help`".
- Check the installed CLI (10.0.12) and replace the `[OPEN]` marker with the verified command, or with an
  explicit statement that the subcommand does not exist.

*Acceptance:* the run output is quoted; `docs/architecture.md` no longer carries the `[OPEN]` marker on
that line.

### Non-functional

**NFR-001 — the factory and the runtime cannot drift**

The snake_case plugin and the provider must be configured through **one** shared code path used by both
the design-time factory and `AddInfrastructure()`. Two copies of
`UseNpgsql(...).UseSnakeCaseNamingConvention()` is a defect, because a change to one silently desyncs the
other. The existing test that asserts exactly one `DbContext` type stays green.

**NFR-002 — the schema is read, not assumed**

Every FR-004 verification is performed against the **server** (`information_schema`, `pg_indexes`,
`pg_constraint`). Reading the migration's C# or the generated script is not sufficient evidence that the
schema applied.

**NFR-003 — no automatic migration on startup**

Nothing calls `Migrate()` or `EnsureCreated()` at startup. `docs/database.md#migrations` states migrations
are applied by an explicit deploy step; `[PLANNED]` lockfile/`--check` guarding is a later task.

## Technical design

### Affected projects and dependency direction

| Project | Change | Does it break `Api` ↛ `Scraper`? |
|---|---|---|
| `TourSearch.Infrastructure` | +`AddInfrastructure()`, +connection/options wiring | no — no new package or project reference |
| `TourSearch.Infrastructure.Migration` | +design-time factory, +`Migrations/` | no — references `Infrastructure` only |
| `tests/TourSearch.UnitTests` | +tests for lifetimes, single-`DbContext`, model/snapshot sync | no |
| `docs/` | `TOURSEARCH_DB_*` names; SQL-export fact | n/a |

`Api` still references only `Core` and `Infrastructure`; nothing here adds `Scraper` anywhere.

### Verified facts (reproduction, 2026-10-15)

Established before writing this task, not assumed:

| Fact | Evidence |
|---|---|
| `dotnet ef` 10.0.12 is installed and runs | `dotnet ef --version` |
| The local server is **PostgreSQL 17.4**, reachable on `127.0.0.1:5432` as `postgres` | `psql -c "select version()"` |
| `uuidv7()` is **absent** | `select proname from pg_proc where proname='uuidv7'` → empty. Confirms PG17 has no native `uuidv7`; `docs/domain.md#guid-ordering`'s warning is correct for this deployment, so `Guid.NewGuid()` stays | 
| Database `tour_search` **was created** for this task | `CREATE DATABASE tour_search` → `CREATE DATABASE`; `pg_database` shows `UTF8`, `datcollate ru-RU` |
| No `tour_search` **schema** exists inside it yet | `pg_namespace` shows only `public` |
| The **Docker daemon is not running** (CLI present) | `docker ps` → `failed to connect to the docker API at npipe://...` |
| `src/TourSearch.Infrastructure.Migration/` contains only a `.csproj` | `find` — no factory, no `Migrations/` |

Consequences to respect:

- **The database already exists**, so the task must not try to create it. EF does **not** create a
  database from `database update` when it is missing in a way that reads clearly; running against a
  missing database fails with a confusing message. The task states `tour_search` as a precondition.
- **No Testcontainers**, because there is no daemon (see *Out of scope*).
- **No `uuidv7`**, so nothing in the migration may reference it.

### Connection settings

Read from the environment, never from a committed file:

| Variable | Required | Notes |
|---|---|---|
| `TOURSEARCH_DB_HOST` | yes | e.g. `127.0.0.1` |
| `TOURSEARCH_DB_PORT` | **new in the docs** | e.g. `5432` |
| `TOURSEARCH_DB_NAME` | **new in the docs** | the database is `tour_search` |
| `TOURSEARCH_DB_USER` | yes | |
| `TOURSEARCH_DB_PASSWORD` | yes | never committed, never echoed into a task file or a log |
| `TOURSEARCH_DB_MAX_POOL` | no | `docs/runtime.md` lists it; tune later |

Operator usage — export the values in the shell yourself, then run the command. The values are not
recorded here:

```bash
export TOURSEARCH_DB_HOST=127.0.0.1
export TOURSEARCH_DB_PORT=5432
export TOURSEARCH_DB_NAME=tour_search
export TOURSEARCH_DB_USER=<your user>
export TOURSEARCH_DB_PASSWORD=<your password>

dotnet ef migrations add InitialSchema --project src/TourSearch.Infrastructure.Migration
dotnet ef migrations list           --project src/TourSearch.Infrastructure.Migration
dotnet ef database update           --project src/TourSearch.Infrastructure.Migration
```

Fail-fast behaviour: a missing required variable produces a clear startup error naming the variable
(`docs/runtime.md#startup` — "must fail fast on a bad configuration, not half-start and then loop").

### The single options path

```
BuildDbContextOptions(Action<NpgsqlDbContextOptionsBuilder>? configure)   // one place
        ├── IDesignTimeDbContextFactory.CreateDbContext()  → design-time
        └── AddInfrastructure()                            → runtime
                    │
                    └── UseNpgsql(<from env>) .UseSnakeCaseNamingConvention()
```

This is what NFR-001 requires and what removes task `002`'s residual risk: the snake_case plugin is
chained exactly once, so a missing call cannot make columns `PascalCase` on one path only.

### File plan

```
src/TourSearch.Infrastructure/
├── DependencyInjection.cs                  # AddInfrastructure()
└── TourSearchDbContextFactory.cs           # or: DbContextOptionsFactory — shared options path

src/TourSearch.Infrastructure.Migration/
├── DesignTimeDbContextFactory.cs           # IDesignTimeDbContextFactory<TourSearchDbContext>
└── Migrations/
    ├── 20261015_InitialSchema.cs
    └── TourSearchDbContextModelSnapshot.cs  # generated

tests/TourSearch.UnitTests/
└── Infrastructure/RegistrationTests.cs     # lifetimes + single DbContext + model/snapshot sync
```

Names are indicative; the reviewer cares that the *options path is shared*, not what the file is called.

## Hard rules (AGENTS.md §3)

| Rule | Applies | How the implementation respects it |
|---|---|---|
| `Api` must not depend on `Scraper` | **yes** | no `Scraper` reference is added anywhere; `AddInfrastructure()` lives in `Infrastructure`, which `Api` already references |
| One `TourSearchDbContext` in `Infrastructure` | **yes — the core risk of this task** | the factory consumes the existing context; it must not redeclare it and must contain no `OnModelCreating`. ADR 002. A test asserts one `DbContext` type |
| Exactly one process starts `BackgroundService` (Worker) | **yes** | `AddInfrastructure()` registers **no** hosted service. ADR 004 |
| Job claim = atomic CAS (`Pending → InProgress`, lease + attempt+1) | **partially** | this task creates the schema the CAS runs against — including the `(status, created_at)` index — but implements no claim. ADR 005 |
| Every persisted `DateTime` is UTC | **yes** | the migration must produce `timestamp with time zone` for every timestamp column; verified on the server, not only in the script |
| Transient error must not permanently fail a job | no | no runtime failure handling here; schema only |
| Partial parse = `Completed` + `Degraded` + `Warnings` | **partially** | `warnings text[]` and `degraded` must exist and be `NOT NULL` in the applied schema |
| `get_search_results` is paginated | **partially** | the `(scraped_at, id)` index must exist on the server |
| No secrets in committed files, including examples | **yes — enforced strictly here** | the connection string is built from env vars; no password, no literal, and no fake-looking placeholder appears in any committed file. ADR 003 |
| No test/CI contacts TourVisor.ru | **yes** | tests touch no network at all; the only external system used is the local PostgreSQL, in the manual verification step |
| No UA rotation / proxy / anti-bot evasion | no | nothing scraping-related |
| `Singleton` only for shared state; default `Scoped` | **yes** | `NpgsqlDataSource` → `Singleton` (shared pool); `DbContext` → `Scoped`. Asserted by a test. ADR 015 |
| `CancellationToken` propagates through every layer | **partially** | nothing long-running here; `AddInfrastructure()` must not introduce blocking `.Result`/`Wait()` on async paths |

## Edge cases and error handling

| Case | Expected behaviour | `ErrorCode` | `Retryable` |
|---|---|---|---|
| A required `TOURSEARCH_DB_*` variable is missing | fail fast with an error naming the **variable**, not a generic connection failure | n/a | n/a |
| The password is wrong | the failure is reported as an authentication error; the password is **never** echoed to logs or output | n/a | n/a |
| `tour_search` database does not exist | the error is explicit that the database is missing; the task does not silently create it (it is a documented precondition) | n/a | n/a |
| `database update` is run twice | second run is a no-op and succeeds (FR-005) | n/a | n/a |
| The factory forgets `.UseSnakeCaseNamingConvention()` | the model snapshot would differ from the runtime model; caught by the model/snapshot sync test and by a server check that column names are `snake_case` | n/a | n/a |
| `AddInfrastructure()` is called twice on one `IServiceCollection` | registrations must not duplicate into two contexts/pools; use `TryAdd*` semantics or document that it is called once | n/a | n/a |
| The server is unreachable during verification | report the verification as **not verified**, with the error, rather than claiming success from the generated script | n/a | n/a |
| A hand-edit to the generated migration | allowed only with the reason stated in the migration body (`docs/database.md#migrations`) | n/a | n/a |
| A migration is generated while the model has uncommitted changes | the migration captures them; state this in the report so the snapshot is not mistaken for the committed model | n/a | n/a |

Cancellation: `Не применимо — миграция применяется офлайн, long-running операций нет.` `AddInfrastructure()`
must not block on async calls.

## State transitions

**No `SearchJob` status is written by this task.** The schema becomes able to *store* every documented
transition; nothing performs one. Writing statuses is step 7 (the Worker's claim and recovery pass).

## Docs to update

| File | What changes | Required by |
|---|---|---|
| `docs/runtime.md` | add `TOURSEARCH_DB_PORT` and `TOURSEARCH_DB_NAME` to the variables table; note the fail-fast behaviour | FR-006 |
| `docs/architecture.md` | same variable names; resolve the `[OPEN]` SQL-export marker with the checked command | FR-006, FR-007 |
| `docs/database.md` | only if the applied schema differs from what the doc claims — correct the doc, not the code, and say which | NFR-002 |
| `docs/conventions.md` | only if the "no secrets" rule needs a note about env-var connection strings | — |
| `docs/open-questions.md` | only if this task closes anything; it does not close question 4 (deployment) or 12 (integration parallelization) | — |

## ADRs required

| ADR | Action | Context |
|---|---|---|
| [ADR 039](../../docs/adr/039-region-delete-restrict.md) et al. | none — unchanged | — |
| new `docs/adr/040-shared-dbcontext-options-path.md` | **create** | why the provider + snake_case options live in one shared function consumed by both the design-time factory and `AddInfrastructure()`, and what breaks if they diverge. This records task `002`'s residual risk as a decision |
| new `docs/adr/041-no-automatic-migration-on-startup.md` | **create** | migrations are applied by an explicit step, never by `Migrate()` at startup; records why (two processes could migrate concurrently) and what is still missing (the `[PLANNED]` lockfile guard) |

If either feels like ceremony, say so — but each records a decision a future reader would otherwise have
to reverse-engineer, and `AGENTS.md` §7.5 makes this part of the definition of done.

## Verification

```bash
dotnet build TourSearch.sln
dotnet test TourSearch.sln --verbosity minimal
```

> `[FACT]` `--all-warnings-as-errors` and `--summary=brief` are **not** real .NET 10 SDK options
> (`MSB1001`). Warnings-as-errors comes from `TreatWarningsAsErrors` in `Directory.Build.props`.

Task-specific proof — migrations, always with `--project`:

```bash
dotnet ef migrations add InitialSchema --project src/TourSearch.Infrastructure.Migration
dotnet ef migrations list            --project src/TourSearch.Infrastructure.Migration
dotnet ef database update            --project src/TourSearch.Infrastructure.Migration
```

Apply to the local PostgreSQL 17.4 database `tour_search` (it already exists; do **not** create it), then
verify **on the server**:

```sql
-- tables
select table_name from information_schema.tables where table_schema = 'tour_search';
-- column types that the model pins
select column_name, data_type, udt_name from information_schema.columns
 where table_schema='tour_search' and table_name='search_jobs'
   and column_name in ('parameters','warnings','status','error_code');
-- index column order
select indexname, indexdef from pg_indexes where schemaname='tour_search' order by indexname;
-- FK delete behaviour
select conname, confdeltype from pg_constraint
 where connamespace = 'tour_search'::regnamespace and contype='f';
```

Expected: three tables; `parameters` = `jsonb`; `warnings` = `_text`(`text[]`); `status` =
`character varying(20)`; `error_code` = `character varying(40)`; eight indexes with
`(status, created_at)` and `(scraped_at, id)` in that order; `confdeltype` = `c` (cascade) for
`search_job_id` and `r` (restrict) for `region_id`; all timestamp columns `timestamp with time zone`.

MCP contract check: `Не применимо` — no tool changes. Do not run the inspector, and never against the
live site.

## Definition of done

1. `dotnet build TourSearch.sln` passes with 0 warnings.
2. Tests pass **and actually run** — a non-zero passed count is quoted. No test touches the network;
   Testcontainers is not used (no Docker daemon).
3. The schema is applied to `tour_search` on the local PostgreSQL 17.4 and verified **from the server**,
   not only from the generated script. If the server became unreachable, that is reported as *not
   verified*, not as success.
4. `docs/mcp-tools.md` is unchanged, so the contract still matches field for field.
5. Every decision made here is an ADR: 040 and 041 created and referenced from the docs they affect.

## Repo drift found

| Claim | Where | Reality |
|---|---|---|
| `docs/architecture.md` export flags `[OPEN]` | `docs/architecture.md#migration-commands` | unresolved; FR-007 checks the installed CLI and closes it |
| `TOURSEARCH_DB_*` list without `PORT`/`NAME` | `docs/runtime.md`, `docs/architecture.md` | incomplete — a connection string cannot be built without them; FR-006 fixes |
| `src/TourSearch.Infrastructure.Migration/` has only a `.csproj` | `AGENTS.md` §2 step 6 assumes the factory exists | still true at authoring time; this task creates it |
| Docker CLI installed but daemon not running | assumed by Testcontainers integration tests | `docker ps` fails; integration tests are deferred |
| `uuidv7` unavailable | `docs/domain.md#guid-ordering` flags this `[OPEN]` | confirmed absent on PG 17.4; `Guid.NewGuid()` stays, and this is now a checked fact for this deployment |
| Database `tour_search` created by the orchestrator | not previously stated anywhere | exists, empty (`public` schema only), UTF8 / `ru-RU` collation |

## Open questions

- **Collation `ru-RU` on `tour_search`.** The cluster default is Russian; region slugs and hotel names
  are ASCII-mostly but Cyrillic hotel names are plausible, and collation affects ordering. → proposed
  default: **leave as-is for now**; ordering that matters is by `price`/`stars`/`id`, not by text → 
  blocks: nothing. Revisit if a text sort or a case-insensitive lookup is ever added.
- **`TOURSEARCH_DB_MAX_POOL` default.** Documented but unused in this task. → proposed default: **do not
  set it yet**; the Npgsql default is 5 and the load does not exist → blocks: nothing.
- **Connection-string vs discrete variables.** `NpgsqlDataSourceBuilder` accepts either. → proposed
  default: **discrete variables** (`Host`/`Port`/`Database`/`Username`/`Password`), matching the
  documented `TOURSEARCH_DB_*` names, so no single string containing a secret is ever assembled in a
  committed file → blocks: nothing.
- **Whether `has-pending-model-changes` exists in EF 10.** → proposed default: **try it**; if absent, use
  the model-snapshot test instead and record which was used → blocks: FR-005's exact wording only.
