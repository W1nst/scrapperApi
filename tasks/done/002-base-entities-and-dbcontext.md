# 002 — Base entities and the EF Core model

| | |
|---|---|
| **Status** | `done` |
| **Type** | feature |
| **Created (UTC)** | `2026-10-13` |
| **Affected projects** | `TourSearch.Core`, `TourSearch.Infrastructure`, `tests/TourSearch.UnitTests` |
| **Source request** | > «давай лучше заведем через task-creator задачу для создания базовых ентити и настройки dbcontextа» |

## Summary

Create the four domain entities (`SearchJob`, `SearchParameters`, `TourOffer`, `Region`), the two enums
(`JobStatus`, `JobErrorCode`), and the single `TourSearchDbContext` with its full fluent configuration —
table names, keys, indexes, the JSONB complex property, enum conversions, and delete behaviours.

This is the **first code in the repository**. It is step 5 of [AGENTS.md §2](../../AGENTS.md) and must
exist before any migration (step 6) or worker logic (step 7).

## Problem

`TourSearch.Core` and `TourSearch.Infrastructure` contain nothing but `.csproj` files, and both
`Program.cs` files are placeholders that exit `1`. Nothing in the specification can be implemented,
migrated or tested until the domain model and its persistence shape exist.

Four things are currently wrong or unverified in the specification and are fixed by this task:

1. **The `docs/database.md` `DbContext` draft does not compile.** `mb.UseSnakeCaseNamingConvention()`
   is called on a `ModelBuilder`, but in `EFCore.NamingConventions` 10.0.1 the extension method is
   declared on `DbContextOptionsBuilder`. Verified by reproduction:

   ```
   error CS1929: "ModelBuilder" не содержит определение для "UseSnakeCaseNamingConvention",
   а наиболее подходящий перегруженный метод расширения
   "NamingConventionsExtensions.UseSnakeCaseNamingConvention(DbContextOptionsBuilder, CultureInfo?)"
   требует наличия получателя типа "Microsoft.EntityFrameworkCore.DbContextOptionsBuilder"
   ```

2. **An unconfigured `TourOffer.RegionId → Region` FK silently becomes `ON DELETE CASCADE`.** This
   contradicts the decision that a region is **deactivated, not deleted**
   ([`docs/domain.md#region-catalog`](../../docs/domain.md#region-catalog)): deleting one region would
   delete every offer in it.

3. **`docs/domain.md` contradicts itself on `SearchParameters.Region`.** The field is described with the
   example `"Antalya"` (a label), while the `Region.Id` it must carry is defined as a **slug**
   (`"antalya"`) — the exact value `list_regions` returns and `search_tours.region` accepts.

4. **A `[UNVERIFIED-FOR-EF-10]` marker is open.** `docs/database.md` and ADR 013 assert that
   `List<string>` maps to a native PostgreSQL `text[]` array "for EF 8.0, not for 10.x". That is now
   verified against the installed provider and must be recorded.

## Scope

### In scope

- `TourSearch.Core`: `SearchJob`, `SearchParameters`, `TourOffer`, `Region`, `JobStatus`, `JobErrorCode`
  as **plain POCOs and enums** — no EF attributes, no `using Microsoft.EntityFrameworkCore`.
- `TourSearch.Infrastructure`: `TourSearchDbContext` with the complete `OnModelCreating` — snake_case,
  `tour_search` schema, explicit `ToTable`, keys, all indexed, the `CompositeProperty`→JSONB mapping,
  enum→string conversions, `char`/`varchar` widths, delete behaviours.
- Verify the generated schema by script generation (no database required).
- The first unit tests: entity invariants are **not** tested here (see *Out of scope*), but the
  **UTC-kind assertion** from [`docs/database.md#utc-trap`](../../docs/database.md#utc-trap) and the
  schema-shape assertions are.
- Resolving **open question 12** (test parallelization policy) — see *Consequences* below.
- Correcting the four defects listed under *Problem*, and the `[UNVERIFIED-FOR-EF-10]` marker.

### Out of scope

- **The migration.** `AGENTS.md` §2 step 6 is a separate task: it needs
  `IDesignTimeDbContextFactory` plus a real PostgreSQL, and the schema name must be settled first
  (that part is settled below, the task is not).
- **`Raw` / `Jsonb` on `TourOffer`.** `docs/domain.md` marks the field `[PLANNED]`, and nothing writes
  it yet — the parser arrives at step 9. Adding a column nobody writes or tests would be dead schema,
  and its JSON shape is a parser decision. Stays `[PLANNED]`.
- **`AddInfrastructure()` / DI registration / connection string.** That is runtime configuration
  ([`docs/runtime.md`](../../docs/runtime.md)) and depends on the deployment question, which is still
  blocking. This task configures the **model**, not the runtime graph.
- **Validation of parameter ranges** (`Adults ≥ 1`, `HotelStars 0..5`, `PriceMax ≥ PriceMin`). Decision
  recorded below: it belongs to the MCP tool layer, not the entities.
- **Repository classes**, seeding of `Region`, `CreatedAt`/`ScrapedAt` population, the TTL cleanup job.
- **`ITourScraper`** and anything Playwright-related.
- Frontend, booking, payment, tourist PII, multi-source aggregation, anti-bot evasion — out per
  `AGENTS.md` §8, and nothing here wants them.

## Requirements

### Functional

**FR-001 — the entities exist in `Core`, and `Core` stays dependency-free**

- `SearchJob`, `SearchParameters`, `TourOffer`, `Region` exist under `src/TourSearch.Core/`.
- `SearchJob` has exactly the 21 fields of [`docs/domain.md#searchjob`](../../docs/domain.md#searchjob),
  with the documented nullability.
- `SearchParameters` has exactly the 8 fields of
  [`docs/domain.md#searchparameters-owned--jsonb`](../../docs/domain.md).
- `TourOffer` has exactly the 14 fields of [`docs/domain.md#touroffer`](../../docs/domain.md), **minus
  `Raw`** (out of scope).
- `Region` has exactly the 4 fields of
  [`docs/domain.md#region-catalog`](../../docs/domain.md#region-catalog).
- `TourSearch.Core.csproj` gains **no** `PackageReference` and **no** `ProjectReference`.
- No entity file contains `using Microsoft.EntityFrameworkCore` or any EF attribute.

*Acceptance:* `dotnet build TourSearch.sln` passes; `grep -rn "EntityFrameworkCore" src/TourSearch.Core/`
returns nothing; a test asserts each entity's property count and types via reflection.

**FR-002 — the enums exist and are stored as strings**

- `JobStatus` = `Pending, InProgress, Completed, Failed, Cancelled` — in that declaration order.
- `JobErrorCode` has all 12 codes from
  [`docs/domain.md#error-taxonomy`](../../docs/domain.md#error-taxonomy): `NETWORK_TIMEOUT`,
  `NETWORK_UNREACHABLE`, `HTTP_429`, `HTTP_5XX`, `BROWSER_CRASH`, `LEASE_EXPIRED`, `SELECTOR_NOT_FOUND`,
  `PAGE_STRUCTURE_CHANGED`, `ANTI_BOT_DETECTED`, `PARAM_INVALID`, `JOB_NOT_FOUND`, `INTERNAL`.
- Both are configured `HasConversion<string>()` with an explicit `HasMaxLength`
  (`JobStatus` → 20, `JobErrorCode` → 40), so the column is `character varying`, not `integer`.
- Enum names are stored **verbatim** (`"InProgress"`, `"NETWORK_TIMEOUT"`) — no renaming, no prefixing.

*Acceptance:* the generated `CREATE TABLE` shows `character varying(20)` / `character varying(40)`;
a test round-trips each enum value through the conversion and asserts the exact name.

**FR-003 — `TourSearchDbContext` is the single context, fully configured**

- Exactly one `DbContext` type exists in the solution, in `TourSearch.Infrastructure`.
- `OnModelCreating` applies snake_case naming, `HasDefaultSchema("tour_search")`, and an explicit
  `ToTable("snake_case")` for all three tables — never relying on a derived name.
- `SearchParameters` is mapped as an **EF Core 10 complex type with `ToJson()`**, producing a `jsonb`
  column named `parameters` on `search_jobs` (not a separate table).
- `SearchJob.Offers` is **navigation only** — no column is generated for it.
- `Warnings` maps to a native PostgreSQL `text[]` with no extra configuration.
- Currency columns are `varchar(3)` with `HasMaxLength(3)` — **not** `char(3)`.

*Acceptance:* `context.Database.GenerateCreateScript()` contains `parameters jsonb NOT NULL`,
`warnings text[] NOT NULL`, `currency_code character varying(3)`, and contains no `raw` column and no
`offers` column.

**FR-004 — every documented index exists, in the documented column order**

| Table | Index | Order matters |
|---|---|---|
| `search_jobs` | `(status, created_at)` | equality column **first** |
| `search_jobs` | `(status, lease_expires_at)` | equality column **first** |
| `search_jobs` | `(parameters_hash)` | |
| `tour_offers` | `(search_job_id)` | FK index |
| `tour_offers` | `(region_id)` | `list_regions` counts |
| `tour_offers` | `(scraped_at, id)` | offer pagination cursor |

- `price` and `check_in` are **optional** per `docs/database.md#index-rules`; include them only if
  they cost nothing to add now. If skipped, say so in the outcome.

*Acceptance:* the generated script's `CREATE INDEX` statements match the table above, with
`(status, created_at)` in that order and `(scraped_at, id)` in that order.

**FR-005 — delete behaviours are explicit and correct**

- `TourOffer.SearchJobId → SearchJob`: `DeleteBehavior.Cascade` — deliberate; offers follow their job
  ([`docs/database.md#cleanup-ttl`](../../docs/database.md#cleanup-ttl)).
- `TourOffer.RegionId → Region`: `DeleteBehavior.Restrict` — a region is deactivated, never deleted;
  cascade here would silently delete every offer of that region.
- Both behaviours are asserted, not left to convention.

*Acceptance:* a test walks `context.Model` and asserts both `DeleteBehavior` values exactly.

**FR-006 — the four documented defects are corrected**

- `docs/database.md`: the `OnModelCreating` snippet is corrected to the compiling form
  (`.UseNpgsql(...).UseSnakeCaseNamingConvention()`, not `modelBuilder.UseSnakeCaseNamingConvention()`),
  with a note that the method lives on `DbContextOptionsBuilder` in `EFCore.NamingConventions` 10.0.1.
- `docs/database.md`: the `RegionId` FK is shown with `OnDelete(DeleteBehavior.Restrict)` and the reason.
- `docs/domain.md`: the `SearchParameters.Region` example is corrected from `"Antalya"` to a slug
  (`"antalya"`), matching `Region.Id` and what `list_regions` returns.
- `docs/database.md` + [ADR 013](../../docs/adr/013-warnings-is-a-native-pg-array.md): the
  `[UNVERIFIED-FOR-EF-10]` marker is replaced with the verified fact, naming the versions it was
  verified against.

*Acceptance:* the corrected snippet compiles as written (it is the code this task writes); grep shows
no remaining `[UNVERIFIED-FOR-EF-10]` marker; no `"Antalya"` example remains in the field table.

**FR-007 — the UTC-kind invariant is covered by a test**

- A unit test asserts that every `DateTime`/`DateTime?` property on the persisted entities, when the
  entity is constructed through the supported path, is `DateTimeKind.Utc`.

*Acceptance:* the test fails if a `Local`/`Unspecified` `DateTime` is assigned; the failure message names
the offending property. See `docs/database.md#utc-trap`: Npgsql **throws** on non-UTC, so this is a
correctness guard, not a style check.

**FR-008 — open question 12 is resolved**

- `MSTEST0001` is resolved by an assembly-level attribute in a real test file, and the interim
  `<NoWarn>$(NoWarn);MSTEST0001</NoWarn>` is **removed** from `tests/TourSearch.UnitTests/TourSearch.UnitTests.csproj`.
- [ADR 036](../../docs/adr/036-test-parallelization-policy.md) moves from `Proposed` to `Accepted`,
  naming the chosen policy and the reason.
- `docs/open-questions.md` question 12 is marked resolved and links to the ADR.
- The **integration** test project keeps its suppression: it has no test file yet, and its shared
  Testcontainers instance makes parallelization a separate decision. Its comment must keep saying so.

*Acceptance:* `dotnet build TourSearch.sln` passes with the suppression removed from the unit project;
ADR 036 reads `Accepted`; question 12 no longer lists this as open.

### Non-functional

**NFR-001 — no schema surprises**

The generated script is read and diffed against the tables above by a human, not merely produced. Any
column, index or constraint that is present but not documented — or documented but absent — is a
finding, reported in the outcome rather than silently accepted.

**NFR-002 — `AsNoTracking()` by default for reads** is a convention for query code; no query code
exists yet, so this task records it as a note for the repository layer rather than implementing it.

**NFR-003 — the model builds without a database**

Every assertion is made through `GenerateCreateScript()` and `context.Model` — no PostgreSQL connection
is required, and none may be attempted. A test that needs a live database belongs to the integration
project in a later task.

## Technical design

### Affected projects and dependency direction

| Project | Change | Does it break `Api` ↛ `Scraper`? |
|---|---|---|
| `TourSearch.Core` | +4 entities, +2 enums (POCO only) | no — no new references at all |
| `TourSearch.Infrastructure` | +`TourSearchDbContext` | no |
| `tests/TourSearch.UnitTests` | +first test files, +assembly attribute, −`NoWarn` | no |
| `docs/` | 4 defect corrections + ADR 036 status | n/a |

`Core` must stay dependency-free: entities are POCOs and **all** EF configuration lives in
`Infrastructure`'s fluent API. Using EF attributes on the entities would require a
`Microsoft.EntityFrameworkCore` reference in `Core`, violating
[`docs/architecture.md`](../../docs/architecture.md) ("Core depends on nothing but the BCL") — and
`TourSearch.Core.csproj` carries an explicit comment forbidding that without an ADR.

### Verified findings (reproduction, not memory)

All three were produced by a throwaway project referencing the **exact pinned versions**
(`Microsoft.EntityFrameworkCore` 10.0.12, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3,
`EFCore.NamingConventions` 10.0.1), running `Database.GenerateCreateScript()` and reading
`context.Model`. The project was deleted afterwards.

| Finding | Evidence |
|---|---|
| `UseSnakeCaseNamingConvention` is on `DbContextOptionsBuilder`, not `ModelBuilder` | `error CS1929` (see *Problem* 1); compiles when chained after `UseNpgsql(...)` |
| `RegionId` FK defaults to `Cascade` | `FK TourOffer.RegionId -> Region : DeleteBehavior=Cascade Required=True`; `Restrict` after the fix |
| `Warnings` → native `text[]` on EF 10 | `COL SearchJob.Warnings type=text[] clr=List\`1` — closes `[UNVERIFIED-FOR-EF-10]` |
| `parameters` → `jsonb` | `parameters jsonb NOT NULL` in the generated script |
| all `DateTime` → `timestamp with time zone` | `COL SearchJob.CreatedAt type=timestamp with time zone` |
| composite index order is preserved | `CREATE INDEX ix_search_jobs_status_created_at ON ... (status, created_at)` |

Additional detail: with an unconfigured reference, EF also generated the region FK as
`ON DELETE CASCADE` in the `CREATE TABLE` statement, so the defect would have reached the migration
unnoticed.

### The `SearchParameters` JSON mapping

`ComplexProperty(...).ToJson()` (EF Core 10) is used, **not** `OwnsOne(...).ToJson()`. `docs/database.md`
calls the latter "the pre-EF-10 owned-entity form — deprecated", while `docs/domain.md` still says
"owned entity → JSONB". The discrepancy is resolved in favour of `ComplexProperty`, and `docs/domain.md`
is corrected.

Consequence to accept deliberately: a complex type is **not** an entity — it has no identity, cannot be
queried independently, and its members cannot be separate columns. That matches how `Parameters` is
used (read and written with its job, never joined), but it does mean "find all jobs for region X" can
only be served by `ParametersHash` reuse or a later explicit column — not by querying inside the JSON.
If that need appears, it needs its own ADR.

### Validation lives at the tool layer (decision)

Parameter ranges (`Adults ≥ 1`, `Children ≥ 0`, `HotelStars` in `0..5`, `Nights ≥ 1`,
`PriceMax ≥ PriceMin`) are **not** enforced by the entities and **not** enforced by database check
constraints. They are enforced by the MCP tool layer at step 8, which rejects them with
`PARAM_INVALID` before a job exists — `docs/domain.md#error-taxonomy` defines `PARAM_INVALID` as
"rejected before the job exists — tool-level only, **never persisted**".

Reasons this is acceptable and deliberate:

- The only writer of `SearchJob` is `search_tours`. There is no second entry point, no admin UI, no
  import path — so the tool layer is a complete chokepoint.
- No check-constraints are specified anywhere in the specification, so adding them here would be a new
  decision made in passing, not a recorded one.
- The tool layer can return a structured `PARAM_INVALID` with the offending field, which a database
  constraint cannot.

Accepted risk, recorded rather than hidden: invalid data **can** be written by direct SQL or a future
second writer, and no layer will stop it. If a second writer ever appears, check-constraints become
necessary and need an ADR. The entities therefore carry **no** validation logic and **no**
`IValidatableObject`.

### File plan

```
src/TourSearch.Core/
├── Entities/
│   ├── SearchJob.cs
│   ├── SearchParameters.cs
│   ├── TourOffer.cs
│   └── Region.cs
└── Enums/
    ├── JobStatus.cs
    └── JobErrorCode.cs

src/TourSearch.Infrastructure/
├── TourSearchDbContext.cs
└── Configuration/
    ├── SearchJobConfiguration.cs      # optional: one IEntityTypeConfiguration per entity
    ├── TourOfferConfiguration.cs
    └── RegionConfiguration.cs

tests/TourSearch.UnitTests/
├── AssemblyInfo.cs                    # [assembly: ...] — resolves MSTEST0001
├── Entities/UtcKindTests.cs
└── Infrastructure/ModelShapeTests.cs
```

Namespaces: `TourSearch.Core.Entities`, `TourSearch.Core.Enums`,
`TourSearch.Infrastructure`, `TourSearch.Infrastructure.Configuration`.

Note: `TourSearch.UnitTests.csproj` currently references `Core` **and** `Scraper`. The schema-shape
tests need `Infrastructure` (where the context lives), so that reference is added. `Scraper` becomes
relevant when parser fixtures arrive.

## Hard rules (AGENTS.md §3)

| Rule | Applies | How the implementation respects it |
|---|---|---|
| `Api` must not depend on `Scraper` | **yes** (indirectly) | no `Scraper` reference is added to `Core`, `Infrastructure` or the test project's graph in a way that reaches `Api`. `Api` still references only `Core` + `Infrastructure` |
| One `TourSearchDbContext` in `Infrastructure` | **yes — this task creates it** | declared once, in `Infrastructure`; the Migration project is untouched and will reference it, not redeclare it ([ADR 002](../../docs/adr/002-migration-references-runtime-dbcontext.md)) |
| Exactly one process starts `BackgroundService` (Worker) | no | no hosted service, no DI registration in this task |
| Job claim = atomic CAS (`Pending → InProgress`, lease + attempt+1) | **partially** | this task creates the **columns and the `(status, created_at)` index** the CAS needs; the claim statement itself is step 7 and needs [ADR 005](../../docs/adr/005-job-claim-atomic-cas.md)'s mechanism |
| Every persisted `DateTime` is UTC | **yes** | every `DateTime`/`DateTime?` field is documented as UTC; FR-007 adds the enforcement test. Npgsql throws on non-UTC, so a miss is a runtime failure, not a silent corruption |
| Transient error must not permanently fail a job | **yes** | `Retryable` is `bool?` and `ErrorCode` is a nullable enum, so "not yet failed" is representable; the taxonomy is preserved verbatim by FR-002 |
| Partial parse = `Completed` + `Degraded = true` + `Warnings` | **yes** | `Degraded`, `Warnings` (`text[]`) and `OffersCount` all exist on `SearchJob`, so the state is representable without schema change |
| `get_search_results` is paginated | **partially** | the `(scraped_at, id)` index the offer cursor needs is created (FR-004); the pagination logic is step 8 |
| No secrets in committed files, including examples | **yes** | the test connection string, if one is needed at all, is not committed — schema assertions use `GenerateCreateScript()` and `context.Model` and need **no** connection. Any connection string in a test uses an obviously non-secret placeholder or Testcontainers |
| No test/CI contacts TourVisor.ru | **yes** | no scraping, no network in any test; all assertions are in-memory model and script generation |
| No UA rotation / proxy pool / anti-bot evasion | no | nothing scraping-related |
| `Singleton` only for shared state; default `Scoped` | no | no DI registration in this task. Note recorded for step 8: `DbContext` will be `Scoped` |
| `CancellationToken` propagates through every layer | no | no async I/O in this task |

## Edge cases and error handling

| Case | Expected behaviour | `ErrorCode` | `Retryable` |
|---|---|---|---|
| A `DateTime` assigned as `Local`/`Unspecified` | FR-007's test fails, naming the property; at runtime Npgsql throws | n/a | n/a |
| `Warnings` left null | entity initialises it to an empty list; the column is `NOT NULL` | n/a | n/a |
| `Offers` left null | initialised to an empty list; **no column is generated** | n/a | n/a |
| `Parameters` left null | `null!` with a required complex property; the column is `NOT NULL`. Callers must supply it — an unset value fails at save, not at compile | n/a | n/a |
| `Region.Id` empty string | accepted by the model; validation is the tool layer's job (see *Validation* above). No check-constraint by decision | n/a | n/a |
| `CurrencyCode` longer than 3 | rejected by `varchar(3)` at the database | n/a | n/a |
| A region is deleted while offers reference it | `Restrict` blocks it — the FK is the enforcement point | n/a | n/a |
| A job is deleted | its offers are deleted by `Cascade` — deliberate | n/a | n/a |
| Enum value not in the taxonomy | cannot be stored: the CLR enum is closed, and the string conversion has no default case to reach | n/a | n/a |
| Two `DbContext` definitions appear | build succeeds but the model drifts; guarded by a test asserting exactly one `DbContext` type in the loaded assemblies | n/a | n/a |

Cancellation: `Не применимо — в этой задаче нет асинхронного ввода-вывода и нет long-running операций.`

## State transitions

**No `SearchJob` status is written by this task.** The status column and its enum are created, and the
transition set below is what the schema must be able to represent — but no code performs a transition.

```
Pending ──claim──▶ InProgress ──┬──▶ Completed  (Degraded possible)
   ▲                            ├──▶ Failed     (Retryable = false)
   │                            └──▶ Pending    (Retryable = true, Attempt+1)
   └── (backoff)
Pending ──▶ Cancelled
InProgress ──(CancelRequestedAt)──▶ Cancelled
InProgress ──(lease expiry)──▶ Pending
```

`CancelRequestedAt`, `LeaseToken`, `LeaseExpiresAt`, `Attempt`, `StartedAt`, `CompletedAt` exist so
every arrow above is representable. Writing them is step 7.

## Docs to update

| File | What changes | Required by |
|---|---|---|
| `docs/database.md` | `UseSnakeCaseNamingConvention` corrected to the compiling form; `RegionId` FK shown `Restrict`; `[UNVERIFIED-FOR-EF-10]` replaced with the verified fact + versions | FR-006 |
| `docs/domain.md` | `SearchParameters.Region` example `"Antalya"` → `"antalya"`; "owned entity" → "EF Core 10 complex type" for `Parameters` | FR-006 |
| `docs/adr/013-warnings-is-a-native-pg-array.md` | `[UNVERIFIED-FOR-EF-10]` closed; `status` stays `Accepted` | FR-006 |
| `docs/adr/036-test-parallelization-policy.md` | `Proposed` → `Accepted`; named policy + reason | FR-008 |
| `docs/open-questions.md` | question 12 marked resolved with a link to ADR 036 | FR-008 |
| `docs/conventions.md` | only if the parallelization policy deserves a line under Testing | FR-008 |
| `docs/agents.md` | only if §2 step 5's gate needs marking satisfied | — |

## ADRs required

| ADR | Action | Context |
|---|---|---|
| [ADR 036](../../docs/adr/036-test-parallelization-policy.md) | **update** `Proposed` → `Accepted` | the first real test forces the policy; FR-008 |
| new `docs/adr/037-parameters-mapped-as-complex-type.md` | **create** | `ComplexProperty().ToJson()` vs `OwnsOne().ToJson()`, and the accepted limitation that JSON members cannot be queried as columns. This is a non-obvious, lasting decision that `docs/domain.md` currently contradicts |
| new `docs/adr/038-parameter-validation-at-tool-layer.md` | **create** | validation deliberately not in entities and not in the database; records the accepted risk that a second writer could store invalid data |
| new `docs/adr/039-region-delete-restrict.md` | **create** | `Restrict` on `RegionId` versus EF's `Cascade` default, tied to "regions are deactivated, not deleted" |

If any of the last three feels like ceremony, say so — but each records a decision that a future reader
would otherwise have to reverse-engineer from the code, and `AGENTS.md` §7.5 makes this part of the
definition of done.

## Verification

```bash
dotnet build TourSearch.sln
dotnet test TourSearch.sln --verbosity minimal
```

> `[FACT]` `--all-warnings-as-errors` and `--summary=brief` are **not** real .NET 10 SDK options
> (`MSB1001`). Warnings-as-errors comes from `TreatWarningsAsErrors` in `Directory.Build.props`. Use
> the commands verbatim.

**After this task, `dotnet test` must no longer be vacuous.** It previously exited `0` while running
zero tests, which `docs/conventions.md#testing` and `AGENTS.md` §7 item 2 both call out as proving
nothing. The tests added here must actually run and be counted — quote the discovered/passed counts.

Task-specific proof:

- `dotnet test TourSearch.sln --verbosity minimal` reports a **non-zero** passed count.
- Schema-shape tests assert, from `GenerateCreateScript()`:
  - `parameters jsonb NOT NULL` present, and no `parameters` table;
  - `warnings text[] NOT NULL` present;
  - `currency_code character varying(3)` present (not `char(3)`);
  - no `raw` column, no `offers` column on `search_jobs`;
  - the six indexes of FR-004, with `(status, created_at)` and `(scraped_at, id)` in that order.
- Model tests assert: exactly one `DbContext` type; both delete behaviours (FR-005); each enum value
  converts to its exact name (FR-002).
- UTC test (FR-007) fails when a `Local` `DateTime` is assigned.
- `grep -rn "EntityFrameworkCore" src/TourSearch.Core/` → empty.
- `grep -rn "UNVERIFIED-FOR-EF-10" docs/` → empty.
- `grep -rn '"Antalya"' docs/domain.md` → empty.

No database is contacted. No migration is generated — that is task 003:

```bash
# not applicable here — the migration is a separate task (AGENTS.md §2 step 6)
```

MCP contract check: `Не применимо` — no tool is added or changed. No `inspector` run, and none against
the live site.

## Definition of done

1. `dotnet build TourSearch.sln` passes with 0 warnings (`TreatWarningsAsErrors` is on).
2. Tests pass **and actually run** — a non-zero passed count is quoted. No test touches the network;
   the only asset is the model and the generated script.
3. No DB state is written: no status transitions, no ad-hoc writes. The schema merely *can* represent
   the documented transitions.
4. `docs/mcp-tools.md` is unchanged, so its contract still matches field for field. No `outputSchema`
   is affected.
5. Every decision made here is an ADR: 036 updated, 037–039 created, and each referenced from the docs
   it affects.

## Repo drift found

Recorded while preparing this task. Verify before starting.

| Claim | Where | Reality |
|---|---|---|
| `modelBuilder.UseSnakeCaseNamingConvention()` | `docs/database.md` DbContext draft | **does not compile** with `EFCore.NamingConventions` 10.0.1 — the method is on `DbContextOptionsBuilder` (FR-006) |
| `RegionId` FK unconfigured | `docs/database.md` is silent on its delete behaviour | EF defaults to `Cascade`, contradicting `domain.md`'s "deactivated, not deleted" (FR-005, FR-006) |
| `SearchParameters.Region` example `"Antalya"` | `docs/domain.md` | contradicts `Region.Id` being a slug (FR-006) |
| "owned entity → JSONB" | `docs/domain.md` | contradicts `docs/database.md`'s `ComplexProperty` guidance; `database.md` wins (FR-006) |
| `[UNVERIFIED-FOR-EF-10]` on `Warnings` | `docs/database.md`, ADR 013 | now verified against EF Core 10.0.12 / Npgsql 10.0.3 (FR-006) |
| `Raw` is `[PLANNED]` | `docs/domain.md` | confirmed still true — the field must **not** be added yet (out of scope) |
| `dotnet test` exits `0` with zero tests | `docs/conventions.md#testing` | still true at authoring time; this task is what changes it (FR-008) |
| integration test project still suppresses `MSTEST0001` | both `tests/*/*.csproj` | the unit project's suppression is removed (FR-008); the integration project's stays, because it still has no test file and its shared Testcontainers instance makes the policy a separate decision |

## Open questions

- **`price` and `check_in` indexes on `tour_offers`.** `docs/database.md#index-rules` marks them
  "optional — only if the agent filters/sorts offers", and that filtering is `[OPEN]` in
  `docs/mcp-tools.md#pagination--mandatory`. → proposed default: **create them now**, because the table
  is empty and an extra index costs nothing at this size; if the sort is never exposed, drop them in a
  later migration → blocks: nothing.
- **`ParametersHash` column width.** Not specified in the docs. SHA-256 hex is 64 characters;
  `docs/domain.md#identity-and-dedupe` shows the format as `sha256:...`, which is 71. → proposed
  default: **`HasMaxLength(71)`** → blocks: nothing. If the prefix is dropped, the width shrinks.
- **Test parallelization policy for the unit project.** FR-008 requires a choice; the task does not
  prescribe which. → proposed default: **parallelize at method level**
  (`[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`), since these tests are pure in-memory
  and share no state; the integration project decides separately later → blocks: FR-008, which is why
  it must be answered during implementation, not deferred.
- **Whether `Region.Label` needs a maximum length.** Not specified. → proposed default: **leave
  unbounded `text`** → blocks: nothing.
- **`Scraper` reference in `TourSearch.UnitTests.csproj`.** It is currently referenced but unused (no
  parser fixtures yet). → proposed default: **leave it**; removing it is unrelated churn → blocks:
  nothing.

## Outcome

| | |
|---|---|
| **Completed (UTC)** | `2026-10-13` |
| **Commit** | `79548c8` — `feat: add base entities and the EF Core model (tasks/002)` |
| **Acceptance** | `FR-001`–`FR-008`, `NFR-001`, `NFR-003` pass; `dotnet build` 0/0; `dotnet test` **18 passed, 0 failed** (was 0 executed before) |
| **ADRs** | [036](../../docs/adr/036-test-parallelization-policy.md) → `Accepted`; [037](../../docs/adr/037-parameters-mapped-as-complex-type.md), [038](../../docs/adr/038-parameter-validation-at-tool-layer.md), [039](../../docs/adr/039-region-delete-restrict.md) created |
| **Left open** | integration-project parallelization (question 12, partly open); migration is task `003` |

### Evidence

| Criterion | Check | Result |
|---|---|---|
| `FR-001` | `grep -rn EntityFrameworkCore src/TourSearch.Core/`; `Core.csproj` unchanged | empty / unchanged |
| `FR-002` | generated DDL | `status varying(20)`, `error_code varying(40)` |
| `FR-003` | independent `GenerateCreateScript()` run by the orchestrator | `parameters jsonb NOT NULL`, `warnings text[] NOT NULL`, `currency_code varying(3)` |
| `FR-004` | same DDL | 8 indexes; `(status, created_at)` and `(scraped_at, id)` in the required order |
| `FR-005` | DDL + `context.Model` walk | `fk_tour_offers_regions … ON DELETE RESTRICT`, `…search_jobs … ON DELETE CASCADE` |
| `FR-006` | greps over `docs/` | four defects corrected; only intentional historical references remain |
| `FR-007` | `UtcKindTests` | the third test injects `Local`/`Unspecified` and **requires** detection — the guard is not vacuous |
| `FR-008` | attribute present, `NoWarn` gone from unit csproj | `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`; integration `NoWarn` untouched |
| `NFR-001` | schema read, not only generated | 3 tables, 20/14/4 columns; nothing undocumented, nothing missing |
| `NFR-003` | no connection opened | `GenerateCreateScript()` + `context.Model` only |

### Division of labour

The code was written by the **`task-developer` subagent** (new rule, `AGENTS.md` §0.1). It proved
`dotnet build` → 0/0 and that the 18 tests run. The orchestrator owned the four document corrections
(`FR-006`), ADR 036's status change and ADRs 037–039, the independent schema verification, and this
outcome. The subagent did not verify behaviour and did not commit — as designed.

### Not verified

- Any behaviour of a feature: entities and mapping only, there is no executable logic yet.
- Anything requiring a live PostgreSQL: no migration was generated and nothing connected to a server,
  so types such as `numeric(12,2)` were read from the model, not from a database.

### Decisions and findings

- **Three items were added beyond the FR bullet list**, all reported by the subagent rather than
  hidden: `HasPrecision(12,2)` on `price` (docs say `decimal(12,2)`), `CurrencyCode = "RUB"`
  initialisers (the `[DECIDED]` default), and the residual snake_case risk below.
- **Residual risk recorded:** snake_case is an options-chain plugin, so **column and index names depend
  on the caller chaining `.UseSnakeCaseNamingConvention()`**. Table names are explicit and safe. The
  future `AddInfrastructure()` must chain it, or columns silently become PascalCase. Documented in
  `docs/database.md` and in the `TourSearchDbContext` XML docs.
- **Defect found during review, not by the subagent:** all 16 relative links in this file were written
  with the `tasks/done/` depth while the file sat in `tasks/`. Fixed here, and the rule is now written
  down (see below).

### Left open

- **Integration-project parallelization** remains undecided: shared Testcontainers instance. Question 12
  is now marked partly resolved; ADR 036 is a precedent for that project, not a template.
- **The migration (task `003`)** — `AGENTS.md` §2 step 6. Needs an `IDesignTimeDbContextFactory` and the
  `AddInfrastructure()` wiring, which is out of scope here.
- **`Raw`/Jsonb on `TourOffer`** stays `[PLANNED]` — a parser decision, step 9.
