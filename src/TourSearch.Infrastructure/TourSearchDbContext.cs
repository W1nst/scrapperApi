using Microsoft.EntityFrameworkCore;
using TourSearch.Core.Entities;

namespace TourSearch.Infrastructure;

/// <summary>
/// The one and only <see cref="DbContext"/> of the system: every table of the TourSearch
/// schema, and the only place the persistence shape is defined (docs/architecture.md →
/// ADR 002). The migration project references this type through a design-time factory; it
/// must never redeclare the model.
/// </summary>
/// <remarks>
/// <para>
/// Table names are explicit below (<c>ToTable("snake_case")</c>), while column and index
/// names come from the <c>EFCore.NamingConventions</c> snake_case plugin. That plugin is an
/// option, not a <c>ModelBuilder</c> extension: it must be chained onto the options builder
/// —
/// <c>new DbContextOptionsBuilder&lt;TourSearchDbContext&gt;().UseNpgsql(...).UseSnakeCaseNamingConvention()</c>
/// — and <c>modelBuilder.UseSnakeCaseNamingConvention()</c> does not compile
/// (CS1929, EF 10.0.1). Wiring it into the application graph is <c>AddInfrastructure()</c>,
/// which is deliberately not part of this type.
/// </para>
/// <para>
/// Registration lifetime is <c>Scoped</c> (docs/conventions.md#di); no hosted service is
/// started here — the Worker is the only process that starts a <c>BackgroundService</c>.
/// </para>
/// </remarks>
public class TourSearchDbContext : DbContext
{
    /// <summary>Creates the context with the options the caller configured.</summary>
    /// <param name="options">
    /// Provider and naming options. Tests build them with
    /// <c>UseNpgsql(...).UseSnakeCaseNamingConvention()</c>; no connection is opened by the
    /// model itself.
    /// </param>
    public TourSearchDbContext(DbContextOptions<TourSearchDbContext> options)
        : base(options)
    {
    }

    /// <summary>Search jobs — the worker's queue and the caller's job records.</summary>
    public DbSet<SearchJob> SearchJobs => Set<SearchJob>();

    /// <summary>Offers found by a search job.</summary>
    public DbSet<TourOffer> TourOffers => Set<TourOffer>();

    /// <summary>Region catalog backing <c>list_regions</c>.</summary>
    public DbSet<Region> Regions => Set<Region>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema("tour_search");

        ConfigureSearchJob(modelBuilder);
        ConfigureTourOffer(modelBuilder);
        ConfigureRegion(modelBuilder);
    }

    private static void ConfigureSearchJob(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SearchJob>(b =>
        {
            b.ToTable("search_jobs");
            b.HasKey(x => x.Id);

            // SHA-256 hex of the canonical parameter JSON, with the "sha256:" prefix the
            // docs show: 64 + 7 = 71.
            b.Property(x => x.ParametersHash).HasMaxLength(71);

            // varchar(3), not char(3): the code is a value, not a fixed-width field.
            b.Property(x => x.CurrencyCode).HasMaxLength(3);

            // Enums are stored as their member names, never as integers.
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.ErrorCode).HasConversion<string>().HasMaxLength(40);

            // EF Core 10 complex type → a single jsonb column on this table. The
            // pre-EF-10 OwnsOne(...).ToJson() form is deprecated (docs/database.md).
            b.ComplexProperty(x => x.Parameters, p => p.ToJson());

            // Job pickup: WHERE status = 'Pending' ORDER BY created_at — the equality
            // column comes first, or the index cannot be seeked.
            b.HasIndex(x => new { x.Status, x.CreatedAt });

            // Lease recovery: WHERE status = 'InProgress' AND lease_expires_at < now.
            b.HasIndex(x => new { x.Status, x.LeaseExpiresAt });

            // Dedupe/idempotency lookup.
            b.HasIndex(x => x.ParametersHash);
        });
    }

    private static void ConfigureTourOffer(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TourOffer>(b =>
        {
            b.ToTable("tour_offers");
            b.HasKey(x => x.Id);

            b.Property(x => x.HotelName).IsRequired();
            b.Property(x => x.RegionId).IsRequired();
            b.Property(x => x.CurrencyCode).HasMaxLength(3);
            b.Property(x => x.Price).HasPrecision(12, 2);

            // Offers follow their job: deleting a search_job deletes its offers
            // (docs/database.md#cleanup-ttl). SearchJob.Offers is a navigation only —
            // no column is generated for it.
            b.HasOne<SearchJob>()
                .WithMany(x => x.Offers)
                .HasForeignKey(x => x.SearchJobId)
                .OnDelete(DeleteBehavior.Cascade);

            // A region is deactivated, never deleted, so this FK must NOT cascade —
            // EF's default here is Cascade, which would delete every offer of the region.
            b.HasOne<Region>()
                .WithMany()
                .HasForeignKey(x => x.RegionId)
                .OnDelete(DeleteBehavior.Restrict);

            // FK index.
            b.HasIndex(x => x.SearchJobId);

            // list_regions counts and the region filter.
            b.HasIndex(x => x.RegionId);

            // The offer pagination cursor: (scraped_at, id).
            b.HasIndex(x => new { x.ScrapedAt, x.Id });

            // Optional indexes, created while the table is empty — cheap now, droppable later.
            b.HasIndex(x => x.Price);
            b.HasIndex(x => x.CheckIn);
        });
    }

    private static void ConfigureRegion(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Region>(b =>
        {
            b.ToTable("regions");
            b.HasKey(x => x.Id);

            // Label stays unbounded text by decision — region names have no agreed ceiling.
            b.Property(x => x.Source).IsRequired();

            // The source's own id ("22"), not Region.Id: the catalog is identified locally by a
            // slug the agent can read, and remotely by this value (ADR 045). varchar(16) rather
            // than an integer: it is an opaque source value, and the source is free to change its
            // scheme.
            b.Property(x => x.SourceId).IsRequired().HasMaxLength(Region.SourceIdMaxLength);

            // Two regions must not claim the same id of the same source.
            b.HasIndex(x => new { x.Source, x.SourceId }).IsUnique();

            // Nesting: a region's parent is another region, so this is a self-referencing FK.
            // Restrict, exactly like TourOffer.RegionId: a region is deactivated, never deleted
            // (ADR 039), so deleting a parent must not silently delete its children.
            b.HasOne<Region>()
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
