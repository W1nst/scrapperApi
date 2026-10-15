# 005 — The region catalog and a working `list_regions`

| | |
|---|---|
| **Status** | `done` |
| **Type** | feature |
| **Created (UTC)** | `2026-10-15` |
| **Affected projects** | `TourSearch.Core`, `TourSearch.Infrastructure`, `TourSearch.Infrastructure.Migration`, `TourSearch.Api`, `tests/TourSearch.UnitTests` |
| **Source request** | > «каталог регионов давай» → регионы Турции собраны с tourvisor.ru |

## Summary

Populate the `Region` catalog with the **85 real Turkish regions** (20 parent + 65 child) collected from
TourVisor, extend the entity with the two fields that data requires (`SourceId`, `ParentId`), seed it
by migration, and replace the `list_regions` stub with a working tool that reads the catalog and computes
`offers_count` from the database.

This is the dependency for everything else: `search_tours` accepts only a region id that `list_regions`
returned (ADR 010), and `TourOffer.RegionId` is a foreign key to this table (ADR 011).

## Problem

`Region` exists as a table and an entity, but nothing populates it and `list_regions` is a stub
(ADR 044). Two concrete gaps block progress:

1. **No data.** The `regions` table is empty, so even a wired-up tool would return `[]` — which is
   indistinguishable from "no such region", the exact confusion ADR 010 exists to prevent.
2. **The entity cannot hold the real data.** `Region` has `Id`, `Label`, `Source`, `Active`. The source
   identifies a region by a **numeric id** and expresses **nesting** via `parentid`; neither is
   representable. `Source` is already taken ("which source this region belongs to", ADR 011) and must not
   be overloaded.

### Verified source data (reproduction, 2026-10-15)

`[FACT]` The region catalog comes from an unauthenticated JSON endpoint of the site:

```
https://tourvisor.ru/xml/listdev.php?type=allcountry,country,region,subregions&formmode=1&format=json
```

- Status 200, ~97 KB, no authentication, no key, no cookie required.
- Shape: `lists.regions.region[]` of `{ id, name, country, parentid, airport? }`.
- **Turkey is `country: 4`** — 85 regions: **20 parents** (`parentid == 0`) and **65 children**.
- Ids are **unique across all 85** (verified).
- The site's own search uses the numeric id: `tourvisor.ru/search.php?region=22` (Kemer). `[FACT]` the
  search API `xml/search.php?region=22` returns `Authorisation Error` — so the **dump** endpoint is open
  while **search** is not. Region collection therefore stays inside the honest-UA policy
  (ADR 014): no evasion, no proxy, no UA rotation.

Collected data is checked in under `.tmp-scrape/` for reference during implementation (gitignored via the
`.tmp-*` rule); the durable copy is `selectors.json` plus the migration.

| Parent | id | Children |
|---|---|---|
| Аланья | 19 | 9 |
| Анталья | 20 | 6 |
| Белек | 21 | 4 |
| Бодрум | 24 | 6 |
| Болу | 669 | 0 |
| Бурса | 268 | 2 |
| Даламан | 25 | 2 |
| Дидим | 155 | 0 |
| Измир | 306 | 0 |
| Кайсери | 586 | 2 |
| Каппадокия | 599 | 2 |
| Кемер | 22 | 6 |
| Кушадасы | 154 | 0 |
| Мармарис | 26 | 4 |
| Сарыкамыш | 667 | 0 |
| Сиде | 23 | 9 |
| Стамбул | 277 | 12 |
| Фетхие | 27 | 1 |
| Чешме | 156 | 0 |
| Эрзурум | 278 | 0 |

## Scope

### In scope

- `Region`: add **`SourceId`** (the source's own numeric identifier, e.g. `"22"`) and **`ParentId`**
  (nullable, the source's `parentid`; `null` for a top-level region). `Source` keeps its existing
  meaning.
- `selectors.json`: create it minimal — `schema_version`, `source`, `updated_at`, `regions[]`, with the
  85 regions and Arabic-numeral-free slugs. Selectors themselves are step 9.
- A migration that seeds all 85 regions, **idempotently**.
- `list_regions`: working implementation reading the catalog and computing `offers_count` per region.
- Tests: slug/seed integrity, nesting, `offers_count` computation, `query` filtering, `Active` filtering.

### Out of scope

- **The scraper and the selectors themselves.** `selectors.json` gets only the regions section; `pages`,
  `fields` and `fallback_selectors` are step 9 (`AGENTS.md` §2 step 9).
- **Fetching the region list at runtime.** The list is collected once and committed. The application must
  not call `listdev.php` — a runtime dependency on a scraping endpoint is exactly what the catalog
  exists to avoid, and it would add an external call to every startup.
- **`search_tours` behaviour.** It stays a stub; this task does not create jobs.
- **Scraping or searching TourVisor.** Only the one documented dump endpoint was read, once, during
  planning. No test or CI run may contact the site (`AGENTS.md` §3).
- **Auth** — still open (question 2).
- **Changing the MCP response shape.** `list_regions` output stays exactly as `docs/mcp-tools.md`
  specifies.
- Frontend, booking, payment, tourist PII, anti-bot evasion — `AGENTS.md` §8.

## Requirements

### Functional

**FR-001 — `Region` carries the source's identity and nesting**

- `Region.SourceId` (`string`, required) holds the source's numeric id as a string, e.g. `"22"`.
- `Region.ParentId` (`string?`) holds the parent's **`Region.Id` slug**, not the source's numeric id, so
  the column is a self-referencing FK to `Region.Id`; `null` means top-level.
- `Region.Source` keeps its existing meaning — which source this is (`"tourvisor"`) — and is **not**
  overloaded with the numeric id.
- `Region.Id` remains a **slug** (`"kemer"`), because it is the value the agent sends back and reads in
  logs; a bare `"22"` is indistinguishable from a typo (decision recorded in ADR 045).

*Acceptance:* a test asserts `SourceId` is non-empty and numeric for every seeded region; a test asserts
every non-null `ParentId` resolves to an existing `Region.Id`; the FK is enforced on the server.

**FR-002 — all 85 regions are in the catalog, with correct nesting**

- Exactly **85** regions are seeded: 20 with `ParentId == null`, 65 with a parent.
- Every parent's child count matches the table above (9 for Аланья, 12 for Стамбул, 0 for the seven
  childless ones, …).
- Slugs are lowercase ASCII, hyphenated, and **unique** (verified: the transliteration produces no
  collisions across both parents and children).
- `Label` is the source's Cyrillic name verbatim (`"Кемер"`, `"Бодрум - центр"`).
- `SourceId` round-trips to the source's id from the collected data, so a parser can build
  `?region={SourceId}`.

*Acceptance:* a test asserts the count, the parent/child split, slug uniqueness, and that a spot-check of
5 regions maps slug → `SourceId` correctly (`kemer`→`"22"`, `alanya`→`"19"`, `istanbul`→`"277"`,
`side`→`"23"`, `belek`→`"21"`).

**FR-003 — `selectors.json` exists, holds the regions, and is validated**

- `selectors.json` exists at the repository root with `schema_version` (integer), `source`, `updated_at`,
  and `regions[]`.
- Each entry carries `id` (slug), `source_id`, `label`, and optional `parent_id`.
- It is validated against a **minimal JSON Schema** at startup; a malformed file fails startup rather
  than the first job (`docs/scraping.md#selectors`).
- The file contains **no** `pages`/`fields`/`fallback_selectors` yet — those arrive in step 9.
- `schema_version` starts at `1`.

*Acceptance:* a test loads and validates the committed file; a test asserts a deliberately broken file
fails validation with a clear message.

**FR-004 — the seed is idempotent and applied by migration**

- A migration seeds all 85 regions. Re-running `database update` applies nothing twice and does not
  violate a key (`docs/database.md#migrations`).
- The seed data is **embedded in the migration**; the migration does not read `selectors.json` at
  runtime — migrations must be reproducible without the working tree's config.
- Seeding is an `INSERT ... ON CONFLICT (id) DO UPDATE`, so re-applying corrects a changed label rather
  than failing.
- Seeding does **not** happen at application startup (ADR 041 governs schema; this is data, but the
  migration keeps it consistent with the "explicit step" model).

*Acceptance:* the migration is applied to `tour_search` and `SELECT count(*)` returns 85; running
`database update` a second time applies nothing; the applied rows carry both `source_id` and `parent_id`.

**FR-005 — `list_regions` works and matches the contract exactly**

- Input `query` (optional): filters over `id` and `label`, case-insensitive substring. Absent = all.
- Output matches `docs/mcp-tools.md` field for field: `regions[{id, label, offers_count}]`, `truncated`.
- `offers_count` is **computed** from `tour_offers` grouped by `region_id` — never a stored counter
  (`docs/domain.md#region-catalog`).
- A region with no offers reports `offers_count: 0`, not a missing field.
- Only `Active = true` regions are returned (user decision): a region the source stopped selling must not
  be offered to the agent.
- Results are ordered deterministically (`label` ascending, `id` as tiebreak) so repeated calls are
  stable.
- `truncated` is `false` for the current catalog size, and the tool still reports it honestly if a cap is
  applied.
- The tool takes a `CancellationToken` and forwards it to the query.
- The stub text and the "NOT IMPLEMENTED in this build" description suffix are **removed** for this
  tool only (ADR 044); `search_tours` and `get_search_results` keep theirs.
- `outputSchema` still declares `regions`/`truncated` — unchanged from the stub.

*Acceptance:* the tool is called over real HTTP and the response quoted; with an empty `tour_offers`
table every `offers_count` is `0` (proving the count is real, not fabricated); with a row inserted, that
region's count rises by one.

**FR-006 — the schema change is additive and the existing data survives**

- The migration adds `source_id` (NOT NULL) and `parent_id` (nullable FK) to `regions`. No column is
  dropped or retyped.
- `tour_offers.region_id → regions.id` keeps its `Restrict` behaviour (ADR 039) and is unaffected.
- `[FACT]` the `regions` table is currently **empty**, so adding a NOT NULL column needs no backfill and
  no default — state this in the migration body.

*Acceptance:* the applied DDL shows the two new columns; `pg_constraint` still shows
`r` (RESTRICT) for `tour_offers.region_id`; the self-FK on `parent_id` exists.

### Non-functional

**NFR-001 — the catalog is the single source of truth**

Region names and ids exist in exactly **two** places that must agree: `selectors.json` (the human-editable
source) and the seeded `regions` table (what the tool reads). A test asserts they match, so editing the
file and forgetting the migration fails the build rather than silently serving stale names.

**NFR-002 — the tool is bounded**

`offers_count` for 85 regions must not be 85 separate queries (N+1). One grouped query, served by the
existing `tour_offers.region_id` index (`docs/database.md#index-rules`). The row count is small today, so
the requirement is about shape, not speed — but a per-region loop would be a defect that survives into
production unnoticed.

**NFR-003 — no network at test time**

No test contacts TourVisor. The collected data is committed; tests read files and the local PostgreSQL
only.

## Technical design

### Affected projects and dependency direction

| Project | Change | Does it break `Api` ↛ `Scraper`? |
|---|---|---|
| `TourSearch.Core` | `Region` gains `SourceId`, `ParentId` | no — still POCO, no EF reference |
| `TourSearch.Infrastructure` | region repository + seed-source reading | no |
| `TourSearch.Infrastructure.Migration` | new migration (columns + seed) | no |
| `TourSearch.Api` | `list_regions` reads the catalog instead of stubbing | no |
| `tests/TourSearch.UnitTests` | catalog/seed/tool tests | no |

`Api` still references only `Core` + `Infrastructure`.

### Entity change

```csharp
public string Id { get; set; } = "";          // slug PK, "kemer"        — unchanged
public string Label { get; set; } = "";       // "Кемер"                — unchanged
public string Source { get; set; } = "";      // "tourvisor"            — unchanged meaning
public bool Active { get; set; }              //                        — unchanged
public string SourceId { get; set; } = "";    // NEW: "22", the source's own id
public string? ParentId { get; set; }         // NEW: "alanya" or null; FK → Region.Id
```

Configuration (fluent, in `Infrastructure`):

- `SourceId` → `HasMaxLength(16)`, required, with a **unique** index on `(source, source_id)` so two
  regions cannot claim the same source id.
- `ParentId` → self-referencing FK `HasOne<Region>().WithMany().HasForeignKey(x => x.ParentId)` with
  `DeleteBehavior.Restrict` (consistent with ADR 039: the catalog is deactivated, not deleted).

### Where the seed data lives

The slug↔source-id mapping is committed twice by necessity, and NFR-001 makes a test enforce agreement:

```
selectors.json            (human-editable, validated by JSON Schema)
        │                  ↓  read + asserted equal by a test
migration (embedded literal C#)  →  regions table  →  list_regions
```

A migration that read `selectors.json` would not be reproducible and would make an applied migration
depend on the working tree.

### File plan

```
selectors.json                                  # schema_version, source, updated_at, regions[]
src/TourSearch.Core/Entities/Region.cs           # + SourceId, + ParentId
src/TourSearch.Core/Regions/RegionSeedData.cs    # or generated: slug, sourceId, label, parentId
src/TourSearch.Infrastructure/Regions/RegionRepository.cs   # grouped offers_count query
src/TourSearch.Infrastructure.Migration/Migrations/*_AddRegionCatalog.cs
src/TourSearch.Api/Tools/RegionTools.cs           # real implementation
src/TourSearch.Api/Tools/Contracts/RegionListResponse.cs   # unchanged shape
tests/TourSearch.UnitTests/Regions/*              # seed, slugs, nesting, tool
```

## Hard rules (AGENTS.md §3)

| Rule | Applies | How the implementation respects it |
|---|---|---|
| `Api` must not depend on `Scraper` | **yes** | the catalog is read from the DB; no Playwright, no HTTP call to the site from the API |
| One `TourSearchDbContext` in `Infrastructure` | **yes** | the new FK/index go through the single fluent configuration in `Infrastructure`; the Migration project still only references it (ADR 002) |
| Exactly one process starts `BackgroundService` (Worker) | no | nothing hosted; the repository is `Scoped` |
| Job claim = atomic CAS | no | no job is created or claimed |
| Every persisted `DateTime` is UTC | **yes** | `updated_at` in `selectors.json` is a date-only string; if any timestamp is persisted it is UTC. No `DateTime.Now` |
| Transient error must not permanently fail a job | no | no job lifecycle |
| Partial parse = `Completed` + `Degraded` + `Warnings` | no | nothing scraped |
| `get_search_results` is paginated | no | untouched |
| No secrets in committed files, including examples | **yes** | `selectors.json` carries no credential; the endpoint used needs none |
| No test/CI contacts TourVisor.ru; fixtures under `tests/fixtures/` | **yes — explicitly** | the site was read **once during planning**; no test, build or CI may contact it. Data is committed. `[FACT]` the dump endpoint needed no auth, and the search endpoint returns `Authorisation Error` — nothing evasive was or may be done |
| No UA rotation / proxy pool / anti-bot evasion without legal review + ADR | **yes** | one unauthenticated dump request, honest UA, no retry loop, no evasion. ADR 014 |
| `Singleton` only for shared state; default `Scoped` | **yes** | the region repository is `Scoped`; it resolves the existing `DbContext` |
| `CancellationToken` propagates through every layer | **yes** | `list_regions` → repository → EF query |

## Edge cases and error handling

| Case | Expected behaviour | `ErrorCode` | `Retryable` |
|---|---|---|---|
| `query` matches nothing | `{ "regions": [], "truncated": false }` — a valid empty result, not an error | n/a | n/a |
| `query` is null / absent | all active regions | n/a | n/a |
| `query` is whitespace | treated as absent (not as a filter matching nothing) | n/a | n/a |
| `query` differs in case (`"КЕМЕР"`, `"kemer"`) | case-insensitive match on both `id` and `label` | n/a | n/a |
| `query` contains `%` or `_` | treated literally, **not** as SQL wildcards (EF's `Contains` parameterizes; a hand-written `LIKE` must escape) | n/a | n/a |
| A region has zero offers | `offers_count: 0` | n/a | n/a |
| `tour_offers` is empty | every region reports `0`; the tool still returns the full catalog | n/a | n/a |
| An offer references a region that is `Active = false` | the region is hidden but the count is not lost; the FK still resolves | n/a | n/a |
| A `ParentId` points at a missing slug | impossible: the FK rejects it at insert; a test asserts it pre-emptively | n/a | n/a |
| `selectors.json` is malformed | startup fails with the offending path/key, not at first use | n/a | n/a |
| `selectors.json` and the DB disagree on a label | the NFR-001 test fails | n/a | n/a |
| The DB is unreachable when `list_regions` is called | the tool fails cleanly; it must not return an empty list, which would look like "no regions exist" | n/a | n/a |
| A second pull of the source data returns a renamed region | a migration updates the label (`ON CONFLICT DO UPDATE`); the slug is stable so the agent's stored id keeps working | n/a | n/a |

Cancellation: `list_regions` accepts a `CancellationToken` and passes it to the query; a cancelled request
returns nothing and does not mutate state.

## State transitions

`Не применимо — каталог не участвует в жизненном цикле job. Статусы не пишутся.`

## Docs to update

| File | What changes | Required by |
|---|---|---|
| `docs/domain.md` | `Region` table gains `SourceId` and `ParentId`; state that `Source` ≠ the source's id; record the 85-region Turkey scope | FR-001 |
| `docs/mcp-tools.md` | `list_regions` marked implemented; note that `offers_count` is computed and that only active regions are returned | FR-005 |
| `docs/scraping.md` | the real endpoint and its shape; `{region_id}` uses `Region.SourceId`; note the dump endpoint is unauthenticated while search is not | FR-003, source data |
| `docs/database.md` | the two new columns, the unique `(source, source_id)` index, the self-FK — and that the catalog is seeded by migration, not at startup | FR-004, FR-006 |
| `docs/adr/044` | note that `list_regions` is now live while the other two remain stubs | FR-005 |
| `docs/open-questions.md` | only if something closes; auth stays open | — |

## ADRs required

| ADR | Action | Context |
|---|---|---|
| new `docs/adr/045-region-identity-slug-plus-source-id.md` | **create** | `Region.Id` is a slug for the agent while `Region.SourceId` holds the source's numeric id, and `ParentId` is a self-FK. Records why the id is not the raw `"22"`, why `Source` is not overloaded, and the transliteration rule |
| new `docs/adr/046-region-catalog-seeded-by-migration.md` | **create** | the catalog is seeded by migration from committed data rather than fetched at startup; records the one-off collection, the no-runtime-fetch rule, and how `selectors.json` stays the editable source without making a migration non-reproducible |

If either feels like ceremony, say so — but ADR 045 records an identity decision that every future tool
and the parser depend on, and ADR 046 records why the app never calls the site on startup.

## Verification

```bash
dotnet build TourSearch.sln
dotnet test TourSearch.sln --verbosity minimal
```

> `[FACT]` `--all-warnings-as-errors` and `--summary=brief` are **not** real .NET 10 SDK options
> (`MSB1001`).

Migrations, always with `--project`:

```bash
export TOURSEARCH_DB_HOST=127.0.0.1 TOURSEARCH_DB_PORT=5432 TOURSEARCH_DB_NAME=tour_search
export TOURSEARCH_DB_USER=... TOURSEARCH_DB_PASSWORD=...     # from LocalSecrets.env

dotnet ef migrations add AddRegionCatalog --project src/TourSearch.Infrastructure.Migration
dotnet ef database update                 --project src/TourSearch.Infrastructure.Migration
dotnet ef database update                 --project src/TourSearch.Infrastructure.Migration   # must be a no-op
```

Server verification (the `tour_search` database already exists — do not create it):

```sql
select count(*) from tour_search.regions;                                  -- 85
select count(*) from tour_search.regions where parent_id is null;          -- 20
select count(*) from tour_search.regions where parent_id is not null;      -- 65
select id, source_id, label, parent_id from tour_search.regions where id in
  ('kemer','alanya','istanbul','side','belek');                            -- slug → source id spot-check
select conname, confdeltype::text from pg_constraint
 where connamespace='tour_search'::regnamespace and contype='f';           -- region self-FK = restrict
```

The tool, over real HTTP:

```bash
export ASPNETCORE_URLS=http://127.0.0.1:5080
dotnet run --project src/TourSearch.Api &

curl -s -X POST http://127.0.0.1:5080/mcp -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"list_regions","arguments":{}}}'

# the filter
curl -s -X POST http://127.0.0.1:5080/mcp -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_regions","arguments":{"query":"кемер"}}}'
```

Expected: 85 regions with `offers_count: 0` (empty offers table), and the filtered call returning Кемер.

Then prove `offers_count` is real, not hardcoded: insert one `tour_offers` row for `kemer`, re-call, and
observe `offers_count: 1`; then roll the insert back so the task leaves no data behind.

MCP contract check: the `list_regions` entry in `tools/list` must be byte-identical in shape to the stub
version — only the description's "NOT IMPLEMENTED" suffix is removed.

## Definition of done

1. `dotnet build TourSearch.sln` passes with 0 warnings.
2. Tests pass **and actually run** — a non-zero passed count is quoted. No test contacts the network.
3. The DB state is reachable only through the documented transitions; the seed is applied by migration,
   not by an ad-hoc write, and re-running `database update` changes nothing.
4. `docs/mcp-tools.md` matches the implementation field for field, `outputSchema` included — the
   `list_regions` shape is unchanged from the stub.
5. Every decision made here is an ADR: 045 and 046 created and referenced from the docs they affect.

## Repo drift found

| Claim | Where | Reality |
|---|---|---|
| `Region` has exactly 4 fields | `docs/domain.md#region-catalog` | insufficient for the real data: the source's numeric id and nesting are not representable. FR-001 extends it (ADR 045) |
| `region_id` in the URL template | `docs/scraping.md#selectors` | the real parameter is the source's numeric id (`?region=22`), which must come from `Region.SourceId` |
| "Seeded at startup from `selectors.json`" | `docs/domain.md#region-catalog` | **contradicted** by the user's decision and by ADR 041's spirit; seeding is by migration (ADR 046). The docs are corrected |
| Region list is not specified anywhere | `docs/*` | now collected: 85 Turkish regions, endpoint documented. No region data existed in the repo before |
| `selectors.json` does not exist | `AGENTS.md` §2 step 9 | still true; this task creates it minimally, with regions only |
| `list_regions` is a stub | `docs/adr/044`, `src/TourSearch.Api/Tools/RegionTools.cs` | this task makes it real while the other two stay stubs |
| `regions` table is empty | not stated anywhere | `[FACT]` verified before this task: the table exists (migration 003) with 0 rows, so the NOT NULL column needs no backfill |
| the site's dump endpoint requires no auth; search returns `Authorisation Error` | not stated anywhere | `[FACT]` verified; recorded because it bounds what the scraper may later rely on |

## Open questions

- **Region scope is Turkey only.** The user asked for "all regions of Turkey"; other countries are
  available from the same endpoint. → proposed default: **Turkey only**, `Active = true`, and the
  catalog grows as data, not as schema → blocks: nothing. A second country is a data change (ADR 011).
- **`query` cap / `truncated` behaviour.** `docs/mcp-tools.md` specifies `truncated` but no cap. →
  proposed default: **no cap** at 85 rows, `truncated` always `false`, and the field kept so a future cap
  is not a contract change → blocks: nothing.
- **Whether children should be returned by default.** The response is flat, per the contract, but a child
  like `kemer-tekirova` is arguably noise next to `kemer`. → proposed default: **return all active
  regions, flat**, because `search_tours` accepts any of them and hiding children would make a valid
  source id unreachable → blocks: nothing. Revisit if the catalog grows past a few hundred.
- **`ParentId` slug vs numeric.** Stored as the parent's **slug**, so the FK is to `Region.Id` and stays
  meaningful if the source's numbering changes. → proposed default: slug → blocks: nothing.
- **Slug transliteration rule.** Adopted: lowercase, Cyrillic→Latin, spaces and ` - ` → `-`. → keep as-is;
  recorded in [ADR 045](./../../docs/adr/045-region-identity-slug-plus-source-id.md) — **including the
  correction below** → blocks: nothing.

## Outcome

| | |
|---|---|
| **Completed (UTC)** | `2026-10-15` |
| **Commit** | `a15f006` — `feat: add the region catalog and make list_regions real (tasks/005)` |
| **Acceptance** | `FR-001`–`FR-006`, `NFR-001`–`NFR-003` pass; build 0/0; `dotnet test` **95 passed, 0 failed** (+54); verified on the real DB and over real HTTP |
| **ADRs** | [045](./../../docs/adr/045-region-identity-slug-plus-source-id.md), [046](./../../docs/adr/046-region-catalog-seeded-by-migration.md) created; [044](./../../docs/adr/044-tools-ship-as-stubs-first.md) updated |
| **Left open** | the other two tools stay stubs; auth remains open |

### Evidence

| Criterion | Check | Result |
|---|---|---|
| `FR-001` | entity + migration | `SourceId` required `varchar(16)`, `ParentId` nullable self-FK; `Source` unchanged |
| `FR-002` | `psql` on `tour_search` | **85 regions / 20 parents / 65 children**; `kemer→22`, `alanya→19`, `istanbul→277`, `side→23`, `belek→21` |
| `FR-003` | committed `selectors.json` | `schema_version: 1`, `source: tourvisor`, 85 entries, no `pages`/`fields` sections |
| `FR-004` | second `database update` | `No migrations were applied` — idempotent |
| `FR-005` | real HTTP `tools/call` | 85 regions, `truncated: false`, all `offers_count: 0`; filter `кемер`/`КЕМЕР` → `kemer` + `kemer-tsentr`; `%` → 0 regions |
| `FR-006` | `pg_constraint` | self-FK `r` (RESTRICT); `tour_offers.region_id` still `r` |
| `NFR-001` | test asserting file ↔ seed agreement | pass |
| `NFR-002` | `RegionRepository` | one grouped query, no N+1 |
| `NFR-003` | no network in tests | pass |

`offers_count` proved **computed, not faked**: inserting one `tour_offers` row for `kemer` made the tool
report `1`, and deleting it returned the count to `0`. Test data cleaned up: `tour_offers` 0 rows,
`search_jobs` 0 rows, `regions` 85.

### Corrected claim in this task (my error, found by the implementer)

This task asserted the transliteration "produces no collisions across all 85" — **false**. The check
behind it compared parents and children **separately**, so parent-vs-child collisions were invisible.
85 regions yield only **83 unique slugs**: `bursa` (parent 268 / child 2517) and `kayseri` (parent 586 /
child 2335). The uniqueness check must span the **whole** key space.

Resolved by rule, not by special case: a child whose slug collides with its parent's is qualified with
the parent's slug — `bursa-bursa`, `kayseri-kayseri`. [ADR 045](./../../docs/adr/045-region-identity-slug-plus-source-id.md)
records both the rule and the failed check that missed it.

A second naming decision: `Стамбул` → **`istanbul`**, not the mechanical `stambul`. Legitimate only
because the slug is **never sent to the source** — `SourceId` carries the id into the URL — so the slug
is purely agent- and human-facing. The exonym list is bounded to that single entry.

### Fixed a contradiction in the docs

`docs/domain.md` claimed the catalog is *"seeded at startup from selectors.json"*. That would have
reintroduced a runtime dependency on a scraping endpoint and contradicted ADR 041's model. Corrected to
"seeded by migration", recorded in ADR 046.

### Not verified

- **Feature behaviour in general** — the numbers above are evidence, not certification.
- **Runtime filtering/`Active`/order by an automated test.** No unit test may open a database, and the
  integration project still has no tests, so a regression there is caught only by the SQL-shape tests.
- **Label ordering follows the database collation** — deterministic per database, not pinned globally.
- **`label` values are Cyrillic** and were read through a shell that mangled direct UTF-8 arguments; the
  tool responses were re-checked with `\u`-escaped JSON, which is how the filter result was confirmed.

### Left open

- **`search_tours` and `get_search_results` remain stubs** (ADR 044). They should be implemented
  together: `search_tours` creates the job that `get_search_results` reads.
- **Auth (question 2)** still open; `list_recent_searches` and `cancel_search` stay unregistered.
- **Catalog freshness** is manual by design (ADR 046): a region the source adds is invisible until
  someone re-runs the collection and applies a migration.
