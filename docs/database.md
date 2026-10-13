# Database (EF Core 10 + PostgreSQL)

Status legend: [agents.md](./agents.md#status-legend).

## Provider

`Npgsql.EntityFrameworkCore.PostgreSQL`

## Conventions

- **snake_case** for tables and columns — via the community plugin `EFCore.NamingConventions` (`modelBuilder.UseSnakeCaseNamingConvention()`). `[FACT]` the plugin is **not** part of official EF Core and is not Microsoft-supported. Alternative without a plugin: hand-write `ToTable`/`SetName` for every entity.
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

## Indexes

The hot query is the worker's job pickup:

```sql
SELECT id FROM tour_search.search_jobs
  WHERE status = 'Pending' ORDER BY created_at;
```

Two separate indexes on `status` and `created_at` do **not** serve this query well — the planner either filters by a low-cardinality `status` or orders by `created_at` and then filters row by row. Declare a **composite** index:

| Index | Why |
|---|---|
| `search_jobs: (created_at, status)` | job pickup — composite, not two separate indexes |
| `search_jobs: parameters_hash` | dedupe/idempotency lookup |
| `tour_offers: search_job_id` | FK index — EF Core creates it automatically; declare explicitly only to rename it |
| `tour_offers: price`, `check_in` | optional — only if the agent filters/sorts offers |

> Ordering (`created_at DESC`) on a composite index: verify the exact `.Order(...)` API against the installed EF Core version, or write the index in a hand-written migration. `[OPEN]` — do not paste an unverified call into code.

## DbContext

```csharp
public class TourSearchDbContext : DbContext
{
    public DbSet<SearchJob> SearchJobs => Set<SearchJob>();
    public DbSet<TourOffer> TourOffers => Set<TourOffer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseSnakeCaseNamingConvention();   // EFCore.NamingConventions
        modelBuilder.HasDefaultSchema("tour_search");

        modelBuilder.Entity<SearchJob>(b =>
        {
            b.ToTable("search_jobs");
            b.HasKey(x => x.Id);

            // hot query: WHERE status = Pending ORDER BY created_at
            b.HasIndex(x => x.CreatedAt, x => x.Status);
            b.HasIndex(x => x.ParametersHash);

            b.OwnsOne(x => x.Parameters, p => p.ToJson());
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

            b.HasIndex(x => x.Price);
            b.HasIndex(x => x.CheckIn);
        });
    }
}
```

### Where this DbContext lives

`TourSearchDbContext` is declared **once**, in `TourSearch.Infrastructure`. The `...Infrastructure.Migration` project contains only `IDesignTimeDbContextFactory<TourSearchDbContext>` and depends on Infrastructure. Two `OnModelCreating()` definitions would let the migration model drift from the runtime model. See [architecture.md](./architecture.md).

## UTC trap

EF Core Npgsql rejects non-UTC `DateTime` values at runtime. A `DateTime` with `TimeZone.Local`/`Unspecified` throws instead of being silently converted.

- Every `DateTime` written to the DB must be UTC: `DateTime.UtcNow` or `Clock.System.Now` (monotonic-safe) converted with `.WithUtc()`.
- A `DateTime` read back is UTC — do not re-`ToUtc` it.
- Add a unit test that asserts the timezone kind of every persisted field.

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
