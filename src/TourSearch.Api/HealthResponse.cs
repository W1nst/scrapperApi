using System.Text.Json.Serialization;

namespace TourSearch.Api;

/// <summary>The body of <c>GET /health</c> (docs/runtime.md#ports-and-health).</summary>
/// <remarks>
/// The supervisor and MCP clients read one document: who this process is, which revision it
/// reports, and where its MCP endpoint lives. Field names are snake_case, matching the tool
/// schemas in <c>docs/mcp-tools.md</c>.
/// </remarks>
/// <param name="Name">The MCP server name; always <see cref="ServerMetadata.Name"/>.</param>
/// <param name="Version">The server version; always <see cref="ServerMetadata.Version"/>.</param>
/// <param name="ProtocolRevision">The revision this server reports as metadata.</param>
/// <param name="McpPath">The path the MCP endpoint is mapped at.</param>
/// <param name="ProtocolNegotiation">How the transport picks the protocol revision.</param>
public sealed record HealthResponse(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("protocol_revision")] string ProtocolRevision,
    [property: JsonPropertyName("mcp_path")] string McpPath,
    [property: JsonPropertyName("protocol_negotiation")] string ProtocolNegotiation)
{
    /// <summary>
    /// The revision is chosen per request by the transport — the revision the contract names has
    /// no handshake to negotiate with (docs/mcp-tools.md, "Protocol revision").
    /// </summary>
    public const string PerRequestNegotiation = "per-request";

    /// <summary>Builds the health payload for the given endpoint settings.</summary>
    /// <param name="endpoint">The validated endpoint settings of this process.</param>
    /// <returns>The health payload.</returns>
    public static HealthResponse Create(McpEndpointOptions endpoint) => new(
        ServerMetadata.Name,
        ServerMetadata.Version,
        endpoint.AdvertisedProtocolRevision,
        endpoint.Path,
        PerRequestNegotiation);
}
