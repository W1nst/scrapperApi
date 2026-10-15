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
    /// <c>poll_after_ms</c>, never invent a region — and, while this build has no Worker, do not
    /// wait for a result that cannot arrive.
    /// </summary>
    public const string Instructions =
        "TourSearch answers tour-offer questions from a single scraped source. " +
        "Order of work: (1) call list_regions first and choose a region by one of the ids it returns; " +
        "(2) call search_tours with that id — it is non-blocking and returns a job_id and poll_after_ms; " +
        "(3) call get_search_results with that job_id and poll again no sooner than poll_after_ms from the previous answer. " +
        "Never invent a region: a region that list_regions did not return is rejected with PARAM_INVALID, " +
        "and a job for an unknown region would complete with zero offers, which looks exactly like a genuine empty result. " +
        "NO WORKER EXISTS IN THIS BUILD, and get_search_results is still a stub: a job created by " +
        "search_tours stays Pending forever and never produces offers, so do not wait for a result " +
        "that cannot arrive. search_tours also refuses a new job while its GLOBAL active-job limit is " +
        "reached (that limit is shared by every caller, not per caller); repeating an identical request " +
        "is always allowed and reuses a recent completed job instead.";
}
