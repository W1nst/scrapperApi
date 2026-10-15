using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TourSearch.Infrastructure;

namespace TourSearch.Infrastructure.Migration;

/// <summary>
/// The design-time entry point <c>dotnet ef</c> uses to build the one
/// <see cref="TourSearchDbContext"/> of the system.
/// </summary>
/// <remarks>
/// <para>
/// This type exists only because <c>dotnet ef</c> runs in a separate process: it does not see
/// the application's DI graph, so it builds the context itself and reads the
/// <c>TOURSEARCH_DB_*</c> environment variables the shell exported
/// (docs/runtime.md#configuration-and-secrets).
/// </para>
/// <para>
/// The context is <b>referenced, never redeclared</b>, and this project defines no model: it
/// contains only this factory and the migrations. Two <c>OnModelCreating()</c> definitions
/// would let the migration model drift from the runtime model (ADR 002,
/// docs/architecture.md#why-migration-must-not-redeclare-dbcontext-decided).
/// </para>
/// <para>
/// The provider and snake_case chain is not written out here — it comes from
/// <see cref="TourSearchDbContextOptionsFactory"/>, the one place the runtime graph uses too
/// (NFR-001), so both paths build an identical model.
/// </para>
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TourSearchDbContext>
{
    /// <inheritdoc />
    public TourSearchDbContext CreateDbContext(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        // Process-scoped on purpose: `dotnet ef` exits when the command finishes and needs the
        // context for a single command. The data source opens no connection until it is used.
        var dataSource = DbConnectionSettings.FromEnvironment().CreateDataSource();

        var options = TourSearchDbContextOptionsFactory.Create(
            dataSource,
            npgsql => npgsql.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly));

        return new TourSearchDbContext(options);
    }
}
