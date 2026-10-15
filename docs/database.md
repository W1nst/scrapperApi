# Database (EF Core 10 + PostgreSQL)

Status legend: [agents.md](./agents.md#status-legend).

## Provider

`Npgsql.EntityFrameworkCore.PostgreSQL`

## Conventions

- **snake_case** for tables and columns — via the community plugin `EFCore.NamingConventions`. `[FACT]` the plugin is **not** part of official EF Core and is not Microsoft-supported. `[FACT]` a `10.0.1` release exists (published 2026-01-22, targets .NET 10) — see <https://www.nuget.org/packages/EFCore.NamingConventions>; pin the exact version in `*.csproj`, do not assume it.
- `[FACT]` **where** the plugin is applied, verified by reproduction on 10.0.1: it is an extension on **`DbContextOptionsBuilder`**, not on `ModelBuilder`. `modelBuilder.UseSnakeCaseNamingConvention()` does **not** compile —
  `error CS1929: "ModelBuilder" не содержит определение для "UseSnakeCaseNamingConvention"`. Chain it onto the options instead:
  `new DbContextOptionsBuilder<TourSearchDbContext>().UseNpgsql(...).UseSnakeCaseNamingConvention()`. Consequence: column and index names depend on every caller chaining it, so `AddInfrastructure()` must do so. Table names are explicit (`ToTable`) and therefore unaffected.
- **UTC** for every `DateTime`.
- **`AsNoTracking()`** by default for reads.
- **JSONB** for raw scraped payload.

## ⚠️ Schema/table naming — do not trust memory

The previous version of this doc claimed: "in EF Core 10, for many-to-many links and some `ToTable` configurations you must specify `schema` explicitly, otherwise the query fails".

That claim is **unverified** — no source, no error text, no reproduction. Treat it as `[UNVERIFIED]`.

Safe practice instead (this is what we rely on):

- Always `modelBuilder.HasDefaultSchema("tour_search")`.
- Always `b.ToTable("snake_case_name")` explicitly — never rely on the derived name.
- When a real failure appears, reproduce it in a minimal sample, capture the exact exception text, and record it as an ADR. Then the doc states a fact with a source, not a half-remembered warning.

## Index rules

The hot query is the worker's job pickup — and it is a **claim**, not a read (see [Job claim mechanism](#job-claim-mechanism)):

```sql
UPDATE tour_search.search_jobs
  WHERE status = 'Pending' AND NOT EXISTS (...)
  ORDER BY created_at LIMIT 1                 -- one job per claim
  RETURNING id, attempt;
```

Two separate indexes on `status` and `created_at` do **not** serve this query well — the planner either filters by a low-cardinality `status` or orders by `created_at` and then filters row by row. Declare **composite** indexes, and put the **equality** column first:

| Index | Why |
|---|---|
| `search_jobs: (status, created_at)` | job pickup — `status` is the equality predicate, `created_at` is the order. `(created_at, status)` is the wrong order: it cannot seek to `status = 'Pending'` |
| `search_jobs: (status, lease_expires_at)` | lease recovery — `WHERE status = 'InProgress' AND lease_expires_at < now` is otherwise a full-table scan on every recovery pass |
| `search_jobs: parameters_hash` | dedupe/idempotency lookup |
| `tour_offers: search_job_id` | FK index — EF Core creates it automatically; declare explicitly only to rename it |
| `tour_offers: region_id` | `list_regions` counts and the `region` filter — without it the count is a full-table `GROUP BY` |
| `tour_offers: (scraped_at, id)` | the offer pagination cursor — see [mcp-tools.md](./mcp-tools.md#pagination--mandatory) |
| `tour_offers: price`, `check_in` | optional — only if the agent filters/sorts offers |
| `regions: (source, source_id)` | **unique** — two regions must not claim one source id; a duplicate would silently attach offers to the wrong region |
| `regions: parent_id` | the self-FK index, so nesting can be traversed without a scan |

> Ordering on a composite index: verify the exact `.Order(...)` API against the installed EF Core version, or write the index in a hand-written migration. `[OPEN]` — do not paste an unverified call into code.

## DbContext

```csharp
public class TourSearchDbContext : DbContext
{
    public DbSet<SearchJob> SearchJobs => Set<SearchJob>();
    public DbSet<TourOffer> TourOffers => Set<TourOffer>();
    public DbSet<Region> Regions => Set<Region>();   // catalog for list_regions — see domain.md

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // NOTE: snake_case is NOT applied here. UseSnakeCaseNamingConvention() is an
        // extension on DbContextOptionsBuilder in EFCore.NamingConventions 10.0.1, not on
        // ModelBuilder — the modelBuilder call is a CS1929 compile error. It belongs in
        // the options chain: UseNpgsql(...).UseSnakeCaseNamingConvention()
        modelBuilder.HasDefaultSchema("tour_search");

        modelBuilder.Entity<SearchJob>(b =>
        {
            b.ToTable("search_jobs");
            b.HasKey(x => x.Id);

            // hot query: WHERE status = Pending ORDER BY created_at  → equality column first
            b.HasIndex(x => x.Status, x => x.CreatedAt);
            // lease recovery: WHERE status = InProgress AND lease_expires_at < now
            b.HasIndex(x => x.Status, x => x.LeaseExpiresAt);
            b.HasIndex(x => x.ParametersHash);

            // EF Core 10: complex types are the recommended JSON mapping.
            // OwnsOne(...).ToJson() is the pre-EF-10 owned-entity form — deprecated.
            b.ComplexProperty(x => x.Parameters, p => p.ToJson());
        });

        modelBuilder.Entity<TourOffer>(b =>
        {
            b.ToTable("tour_offers");
            b.HasKey(x => x.Id);

            // Offers is navigation only — it is not a column
            b.HasOne<SearchJob>()
             .WithMany(x => x.Offers)
             .HasForeignKey(x => x.SearchJobId)
             .OnDelete(DeleteBehavior.Cascade);   // documented below

            // A region is deactivated, never deleted (domain.md#region-catalog), so this FK
            // must NOT cascade. [FACT] verified by reproduction: an unconfigured reference to
            // Region generates ON DELETE CASCADE here, which would delete every offer of a
            // deleted region. Restrict is mandatory, not a preference.
            b.HasOne<Region>()
             .WithMany()
             .HasForeignKey(x => x.RegionId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(x => x.RegionId);          // list_regions counts + region filter
            b.HasIndex(x => x.ScrapedAt, x => x.Id);  // offer pagination cursor (scraped_at, id)
            b.HasIndex(x => x.Price);
            b.HasIndex(x => x.CheckIn);
        });

        modelBuilder.Entity<Region>(b =>
        {
            b.ToTable("regions");
            b.HasKey(x => x.Id);

            // The source's own numeric id — what a parser puts into ?region={SourceId}.
            // Unique per source, so two regions cannot claim it (ADR 045).
            b.Property(x => x.SourceId).HasMaxLength(16).IsRequired();
            b.HasIndex(x => new { x.Source, x.SourceId }).IsUnique();

            // Nesting: the parent's Region.Id slug. Restrict, like every other catalog FK —
            // a region is deactivated, never deleted (ADR 039/045).
            b.HasOne<Region>()
             .WithMany()
             .HasForeignKey(x => x.ParentId)
             .OnDelete(DeleteBehavior.Restrict);

            b.Property(x => x.Source).IsRequired();
        });
    }
}
```

`[FACT]` `List<string> Warnings` needs no configuration: EF Core maps primitive collections automatically, and on Npgsql they become **native PostgreSQL array columns** (`text[]`), not JSON — see [Warnings column representation](#warnings-column-representation).

### Where this DbContext lives

`TourSearchDbContext` is declared **once**, in `TourSearch.Infrastructure`. The `...Infrastructure.Migration` project contains only `IDesignTimeDbContextFactory<TourSearchDbContext>` and depends on Infrastructure. Two `OnModelCreating()` definitions would let the migration model drift from the runtime model. See [architecture.md](./architecture.md).

## UTC trap

EF Core Npgsql rejects non-UTC `DateTime` values at runtime. A `DateTime` with `TimeZone.Local`/`Unspecified` throws instead of being silently converted.

- Every `DateTime` written to the DB must be UTC: `DateTime.UtcNow` or `Clock.System.Now` (monotonic-safe) converted with `.WithUtc()`.
- A `DateTime` read back is UTC — do not re-`ToUtc` it.
- Add a unit test that asserts the timezone kind of every persisted field.

## `uuid` vs `uuid7`

`[FACT]` PostgreSQL **18** (released 2025-09-10) added a native `uuidv7()` function and `uuid_extract_timestamp()`. PostgreSQL **17 and earlier have neither**, and the third-party `pg_uuidv7` extension is only needed below 18. Sources: Neon "What's new in PostgreSQL 18" and the PostgreSQL 18 release notes.

`[DECIDED]`: not adopted — `Guid.NewGuid()` client-side stays the PK (see [domain.md](./domain.md#guid-ordering)). The deployed PostgreSQL version is not pinned in these docs, so any uuid7 claim must name a version.

## Job claim mechanism

`[DECIDED]` — the claim is an **atomic compare-and-set**, not a read-then-write. Two workers must never pick up the same job; see [runtime.md](./runtime.md#restart-semantics) for the lease rules.

The shape that must be implemented, and the one EF Core does **not** give us:

- EF Core has no atomic CAS across a read and an update. `ExecuteUpdate` writes a row; it does not make "read `Pending` → write `InProgress`" atomic across two statements.
- The claim is therefore **hand-written Npgsql** inside the Infrastructure layer: one statement —
  `UPDATE search_jobs WHERE status = 'Pending' … ORDER BY created_at LIMIT 1 … RETURNING …`,
  expressed with `db.Query<SearchJob>().UpdateFrom<SearchJob>(...)` / `SqlExpression`, wrapped in `db.Transaction(...)` where a second write is part of the claim.
- `LeaseToken` is the claiming worker's id; `Attempt + 1` and `LeaseExpiresAt` are written in the **same** statement.

`[REQUIRED-ADR]` — **satisfied**: see [ADR 005](./adr/005-job-claim-atomic-cas.md), which fixes the exact statement, the transaction boundary, and the recovery pass that returns an expired lease to `Pending`. A half-implemented claim is a system bug, not a style issue.

## Warnings column representation

`List<string> Warnings` on `SearchJob`:

- `[FACT]` EF Core 8+ maps primitive collections (`List<string>`, `string[]`) automatically, and **on Npgsql they map to native PostgreSQL array columns** — so `warnings text[]` is the default, and no child table is needed. Sources: the EF 8.0 What's New primitive-collections section, the Npgsql 8.0 release notes ("the PostgreSQL driver aligned its PostgreSQL array support"), and the Npgsql provider docs. **Verified for EF 10 as well** (2026-10-13, by reproduction): with `Microsoft.EntityFrameworkCore` 10.0.12 + `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 the generated DDL contains `warnings text[] NOT NULL`, and the property's column type reads back as `text[]` with CLR type `List<string>`. The earlier `[UNVERIFIED-FOR-EF-10]` marker is therefore closed.
- Filtering a PG array column is not index-served the way a scalar column is; nothing filters by `Warnings`, so that is acceptable.
- If a JSON array is ever preferred (for example to index into it), use an EF Core 10 complex type with `ToJson()` — not the deprecated `OwnsOne` form. See [JSON Mapping](https://www.npgsql.org/efcore/mapping/json.html).
- A child table (`search_job_warnings`) is **not** used: warnings are read only with their job, never joined, and a child table would make the "warnings belong to this job" rule a convention instead of a type.

## Cleanup (TTL)

| Object | TTL | Decision |
|---|---|---|
| `SearchJob` | 30 days `[DECIDED]` default, configurable | older jobs are not useful to an agent |
| `TourOffer` | follows its job | via `DeleteBehavior.Cascade` |
| HTML snapshots | 7 days `[DECIDED]` default | see [scraping.md](./scraping.md#snapshots) |

`DeleteBehavior.Cascade` is a deliberate decision, not a default: deleting a `search_job` **also deletes its `tour_offers`**. With `None` they would become orphans with a dangling FK. If offers ever need to survive their job (cross-job dedupe/cache reuse), switch to `None` and add explicit cleanup — that is an `[OPEN]` question, not a silent change.

Cleanup rules:

- Run in a `BackgroundService` with a `CancellationToken` — it must stop on shutdown.
- Paginate: `WHERE created_at < cutoff LIMIT 500`, loop, never one unbounded `DELETE`.
- **Never** delete an `InProgress` job whose lease expired — first return it to `Pending` (see [runtime.md](./runtime.md#restart-semantics)), then let normal TTL handle it.
- Log how many objects were deleted per run (metrics, not debug).

## Connection pool

- Tune `Maximum Pool Size` to the real load; the Npgsql default (5) is too low for a worker + API sharing one DB.
- Use `NpgsqlDataSource` (recommended in Npgsql 8+): it carries the timezone handling rules that the UTC trap above depends on.

## Migrations

- Only via CLI: `dotnet ef migrations add <Name> --project src/TourSearch.Infrastructure.Migration`.
- Do not hand-edit generated migrations without a stated reason; if you must, put the reason in the migration body.
- Names by meaning: `AddSearchJobIndexes`, not `Migration1`.
- Never apply migrations in production automatically — an explicit deploy step, with a lockfile/`--check` guard so two deploys cannot migrate at once. `[PLANNED]`
