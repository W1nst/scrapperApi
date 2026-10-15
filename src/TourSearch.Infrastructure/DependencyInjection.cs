using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace TourSearch.Infrastructure;

/// <summary>Registers the database layer of TourSearch into a DI graph.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="TourSearchDbContext"/> (<c>Scoped</c>) and the
    /// <see cref="NpgsqlDataSource"/> pool it runs on (<c>Singleton</c>).
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// A required <c>TOURSEARCH_DB_*</c> variable is missing, blank or malformed. The throw
    /// happens at registration time, so a misconfigured process fails at startup naming the
    /// variable instead of half-starting with a broken context (docs/runtime.md#startup).
    /// </exception>
    /// <remarks>
    /// <para>
    /// Lifetimes: the data source is a pool — shared state, one instance for the whole graph
    /// (<c>Singleton</c>, ADR 015); the context is <c>Scoped</c>, the documented default
    /// (docs/conventions.md#di). Registration goes through the one shared options chain
    /// (<see cref="TourSearchDbContextOptionsFactory"/>), so the runtime model and the migration
    /// model cannot diverge.
    /// </para>
    /// <para>
    /// This method registers no hosted service: only the Worker starts a
    /// <c>BackgroundService</c> (ADR 004). It also applies no migration — no <c>Migrate()</c>,
    /// no <c>EnsureCreated()</c>: migrations are an explicit deploy step (ADR 041,
    /// docs/database.md#migrations).
    /// </para>
    /// </remarks>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var settings = DbConnectionSettings.FromEnvironment();

        // TryAdd semantics: a second AddInfrastructure() call must not open a second pool.
        // The factory itself is never invoked before a scope resolves the context, that is
        // after the process has already validated its configuration above.
        services.TryAddSingleton(_ => settings.CreateDataSource());

        services.AddDbContext<TourSearchDbContext>((provider, options) =>
            TourSearchDbContextOptionsFactory.Configure(
                options,
                provider.GetRequiredService<NpgsqlDataSource>()));

        return services;
    }
}
