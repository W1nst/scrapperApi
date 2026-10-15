using Microsoft.Extensions.Configuration;

namespace TourSearch.Api;

/// <summary>
/// The endpoint settings of the API process: the MCP path and the protocol revision reported as
/// server metadata.
/// </summary>
/// <remarks>
/// <para>
/// The <b>URL</b> is not here on purpose: it is the standard ASP.NET Core
/// <c>ASPNETCORE_URLS</c> setting (default <c>http://localhost:5000</c>), not a
/// <c>TOURSEARCH_*</c> name invented for this process (docs/runtime.md#configuration-and-secrets).
/// </para>
/// <para>
/// <see cref="AdvertisedProtocolRevision"/> is <b>server metadata only</b>: the transport
/// negotiates the revision per request and this value is never written to
/// <c>McpServerOptions.ProtocolVersion</c>, so no client is pinned to it
/// (docs/adr/043-mcp-hosted-over-streamable-http.md).
/// </para>
/// </remarks>
/// <param name="Path">The path the MCP endpoint is mapped at.</param>
/// <param name="AdvertisedProtocolRevision">The protocol revision reported by <c>GET /health</c>.</param>
public sealed record McpEndpointOptions(string Path, string AdvertisedProtocolRevision)
{
    /// <summary>Configuration key of <see cref="Path"/>.</summary>
    public const string PathKey = "Mcp:Path";

    /// <summary>Configuration key of <see cref="AdvertisedProtocolRevision"/>.</summary>
    public const string AdvertisedProtocolRevisionKey = "Mcp:AdvertisedProtocolRevision";

    /// <summary>
    /// Reads and validates the endpoint settings.
    /// </summary>
    /// <param name="configuration">The configuration of the process (appsettings.json + environment).</param>
    /// <returns>The validated settings.</returns>
    /// <exception cref="InvalidOperationException">
    /// A setting is missing or blank, or the path is not an absolute path. The message names the
    /// setting, so a misconfigured start is diagnosable: startup must fail fast, not half-start
    /// and then serve nothing (docs/runtime.md#startup).
    /// </exception>
    public static McpEndpointOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var path = Required(configuration, PathKey);

        if (!path.StartsWith('/'))
        {
            throw new InvalidOperationException(
                $"Configuration value {PathKey} must be an absolute path starting with '/' (found '{path}').");
        }

        return new McpEndpointOptions(path, Required(configuration, AdvertisedProtocolRevisionKey));
    }

    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key];

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required configuration value {key} is not set. TourSearch.Api reads it from " +
                "appsettings.json or from the environment and refuses to start with an incomplete " +
                "configuration (docs/runtime.md#startup).");
        }

        return value;
    }
}
