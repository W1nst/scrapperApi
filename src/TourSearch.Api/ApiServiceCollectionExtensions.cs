using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using TourSearch.Infrastructure;

namespace TourSearch.Api;

/// <summary>Registers the API process: its settings, its database layer and its MCP server.</summary>
public static class ApiServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything the API process resolves: the validated endpoint settings, the
    /// tuning of <c>search_tours</c>, the database layer, and the MCP server with the three
    /// shipped tools.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">
    /// The configuration of the process. A missing or invalid setting throws here, before
    /// anything binds a port (docs/runtime.md#startup).
    /// </param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// A required <c>Mcp:*</c> setting is missing or malformed, a <c>Search:*</c> setting is not a
    /// positive integer, or a required <c>TOURSEARCH_DB_*</c> variable is missing (thrown by
    /// <c>AddInfrastructure()</c>).
    /// </exception>
    /// <remarks>
    /// <para>
    /// The graph is deliberately small. <c>AddInfrastructure()</c> registers the single
    /// <c>TourSearchDbContext</c> (<c>Scoped</c>), its pool (<c>Singleton</c>) and the scoped
    /// repositories the tools read and write through — and <b>nothing
    /// else</b>: no <c>BackgroundService</c> — the Worker is the only process that starts one
    /// (ADR 004) — and no <c>Migrate()</c>/<c>EnsureCreated()</c>, because migrations are an
    /// explicit deploy step (ADR 041). <c>TourSearch.Scraper</c> is not referenced anywhere in
    /// this project or its dependency closure: no Playwright, no Chromium in the API process
    /// (ADR 001).
    /// </para>
    /// <para>
    /// The transport is HTTP, not the SDK's stdio default, and it is <b>stateless</b>: the
    /// protocol revision the contract names keeps no session, so every request is
    /// self-contained and no client has to keep one alive (docs/mcp-tools.md).
    /// </para>
    /// </remarks>
    public static IServiceCollection AddTourSearchApi(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Fail fast on bad configuration, naming the missing piece.
        services.AddSingleton(McpEndpointOptions.FromConfiguration(configuration));

        // The tuning of search_tours (reuse window, active-job limit, poll hint). Registered as one
        // shared, validated instance: it is immutable configuration, read on every call and written
        // never (docs/conventions.md#di).
        services.AddSingleton(SearchOptions.FromConfiguration(configuration));

        services.AddInfrastructure();

        services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = ServerMetadata.Name,
                    Version = ServerMetadata.Version,
                };
                options.ServerInstructions = ServerMetadata.Instructions;
            })
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithTools<Tools.RegionTools>()
            .WithTools<Tools.SearchTools>();

        return services;
    }
}
