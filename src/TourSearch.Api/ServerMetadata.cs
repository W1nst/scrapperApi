using System.Text.Json.Serialization;

namespace TourSearch.Api;

/// <summary>
/// The identity of this server, as the contract in <c>docs/mcp-tools.md</c> defines it: the
/// name an MCP client sees in <c>initialize</c>/<c>server/discover</c>, the server version, and
/// the <c>instructions</c> string handed to the agent.
/// </summary>
/// <remarks>
/// The names here are the contract, not configuration: an agent that caches a tool list keyed by
/// server name must see a stable one. Only the endpoint path and the advertised protocol
/// revision are settings (<see cref="McpEndpointOptions"/>).
/// </remarks>
public static class ServerMetadata
{
    /// <summary>The MCP server name (docs/mcp-tools.md, "Server metadata").</summary>
    public const string Name = "tour-search";

    /// <summary>
    /// The server version reported to MCP clients and by <c>GET /health</c>
    /// (docs/mcp-tools.md, "Server metadata").
    /// </summary>
    public const string Version = "0.0.1";

    /// <summary>
    /// The <c>instructions</c> string sent with the server metadata (docs/mcp-tools.md): call
    /// <c>list_regions</c> before <c>search_tours</c>, poll <c>get_search_results</c> with
    /// <c>poll_after_ms</c>, and never invent a region.
    /// </summary>
    public const string Instructions =
        "TourSearch answers tour-offer questions from a single scraped source. " +
        "Order of work: (1) call list_regions first and choose a region by one of the ids it returns; " +
        "(2) call search_tours with that id — it is non-blocking and returns a job_id and poll_after_ms; " +
        "(3) call get_search_results with that job_id and poll again no sooner than poll_after_ms from the previous answer. " +
        "Never invent a region: a region that list_regions did not return produces a job that completes with zero offers, " +
        "which looks exactly like a genuine empty result. " +
        "In this build the three tools are not implemented yet and say so in their result; " +
        "do not treat a stub result as data.";
}
