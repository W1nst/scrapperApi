using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace TourSearch.Infrastructure;

/// <summary>
/// The single place where the provider and the naming convention of
/// <see cref="TourSearchDbContext"/> are configured.
/// </summary>
/// <remarks>
/// <para>
/// Both the runtime graph (<see cref="DependencyInjection.AddInfrastructure"/>) and the
/// design-time factory (<c>TourSearch.Infrastructure.Migration</c>) build their options through
/// this type, so the migration model and the runtime model cannot drift. A second copy of the
/// chain is a defect: <c>UseSnakeCaseNamingConvention()</c> is an option, not a model
/// convention, so a path that forgets it silently gets <c>PascalCase</c> columns and migrations
/// that no longer match the runtime model (docs/database.md#conventions).
/// </para>
/// </remarks>
public static class TourSearchDbContextOptionsFactory
{
    /// <summary>
    /// Builds the options of <see cref="TourSearchDbContext"/> for the given data source.
    /// </summary>
    /// <param name="dataSource">
    /// The shared Npgsql data source (docs/database.md#connection-pool). The data source
    /// carries the pool and the timezone handling the UTC rule depends on
    /// (docs/database.md#utc-trap).
    /// </param>
    /// <param name="configure">Provider options the caller needs to add, e.g. the migrations assembly.</param>
    /// <returns>Options for a new <see cref="TourSearchDbContext"/>.</returns>
    public static DbContextOptions<TourSearchDbContext> Create(
        NpgsqlDataSource dataSource,
        Action<NpgsqlDbContextOptionsBuilder>? configure = null)
    {
        var builder = new DbContextOptionsBuilder<TourSearchDbContext>();
        Configure(builder, dataSource, configure);

        return builder.Options;
    }

    /// <summary>
    /// Applies the shared options chain to an options builder the caller owns — the shape
    /// <c>AddDbContext</c> hands to its registration callback.
    /// </summary>
    /// <param name="builder">The options builder to configure.</param>
    /// <param name="dataSource">The shared Npgsql data source.</param>
    /// <param name="configure">Provider options the caller needs to add.</param>
    internal static void Configure(
        DbContextOptionsBuilder builder,
        NpgsqlDataSource dataSource,
        Action<NpgsqlDbContextOptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(dataSource);

        // The only provider + naming chain in the code base (ADR 040). Everything that builds
        // a TourSearchDbContext comes through here — see the type remarks.
        builder.UseNpgsql(dataSource, configure);
        builder.UseSnakeCaseNamingConvention();
    }
}
