using System.Net.Sockets;

namespace TourSearch.Api;

/// <summary>
/// The startup sequence of the API process: configuration, the DI graph, the MCP endpoint,
/// the health endpoint, and the exit codes of <c>docs/runtime.md#shutdown</c>.
/// </summary>
/// <remarks>
/// <para>
/// The exit code is <b>returned</b> from <c>Main</c> (see <c>Program.cs</c>), never set with
/// <c>Environment.Exit</c> inside the graph, so the host disposes and the console flushes in
/// order and shutdown goes through the host's own <see cref="CancellationToken"/>
/// (docs/conventions.md#cancellation).
/// </para>
/// <para>
/// Startup fails fast on a bad configuration, naming the missing piece
/// (docs/runtime.md#startup): <c>AddTourSearchApi</c> validates the MCP settings before
/// anything binds a port, and <c>AddInfrastructure()</c> throws naming the missing
/// <c>TOURSEARCH_DB_*</c> variable.
/// </para>
/// </remarks>
public static class ApiProgram
{
    /// <summary>Clean shutdown (docs/runtime.md#shutdown).</summary>
    public const int CleanExitCode = 0;

    /// <summary>Bad configuration: a missing or invalid setting, or an unbindable URL/port.</summary>
    public const int ConfigurationErrorExitCode = 1;

    /// <summary>Unrecoverable internal error: fail fast and let the supervisor restart the process.</summary>
    public const int UnrecoverableErrorExitCode = 2;

    /// <summary>Runs the API process to completion.</summary>
    /// <param name="args">The command-line arguments of the process.</param>
    /// <returns>The exit code of the process (see the constants on this type).</returns>
    public static async Task<int> RunAsync(string[] args)
    {
        // Startup failures happen before the host's logging pipeline exists, so failures are
        // reported through this bootstrap factory. It writes through ILogger (never
        // Console.WriteLine) and is disposed with the process.
        using var bootstrapLoggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole());
        var logger = bootstrapLoggerFactory.CreateLogger(typeof(ApiProgram));

        try
        {
            // The content root is the directory of the application, not the current working
            // directory: a service manager starts the process with an arbitrary working
            // directory, and a deployment that cannot find its own appsettings.json would
            // otherwise refuse to start for a reason that has nothing to do with its
            // configuration.
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = AppContext.BaseDirectory,
            });

            // The whole graph, and the only place configuration is read: Mcp:* plus the
            // TOURSEARCH_DB_* variables that AddInfrastructure() requires.
            builder.Services.AddTourSearchApi(builder.Configuration);

            var app = builder.Build();
            var mcp = app.Services.GetRequiredService<McpEndpointOptions>();

            app.MapMcp(mcp.Path);
            app.MapGet("/health", () => Results.Ok(HealthResponse.Create(mcp)));

            var urls = builder.Configuration[WebHostDefaults.ServerUrlsKey]
                ?? "(ASP.NET Core default: http://localhost:5000)";
            logger.LogInformation(
                "TourSearch.Api starting: MCP at {McpPath}, listening on {Urls}",
                mcp.Path,
                urls);

            await app.RunAsync();

            return CleanExitCode;
        }
        catch (InvalidOperationException exception)
        {
            // Bad configuration: a missing TOURSEARCH_DB_* variable, an empty Mcp:* setting, an
            // MCP path without a leading '/'. Raised before the first request is served.
            logger.LogCritical(exception, "TourSearch.Api refused to start: configuration is invalid.");
            return ConfigurationErrorExitCode;
        }
        catch (IOException exception)
        {
            // Kestrel reports an unbindable URL/port ("address already in use") as an
            // IOException (its AddressInUseException derives from it). Exit 1 with a clear
            // error, never a silent hang (task 004, edge cases).
            logger.LogCritical(exception, "TourSearch.Api refused to start: the configured URL could not be bound.");
            return ConfigurationErrorExitCode;
        }
        catch (SocketException exception)
        {
            // Same failure, unwrapped — the socket layer is not always wrapped in an IOException.
            logger.LogCritical(exception, "TourSearch.Api refused to start: the configured URL could not be bound.");
            return ConfigurationErrorExitCode;
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "TourSearch.Api stopped with an unrecoverable error.");
            return UnrecoverableErrorExitCode;
        }
    }
}
