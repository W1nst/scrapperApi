using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TourSearch.Api.Tools.Contracts;

namespace TourSearch.Api.Tools;

/// <summary>The <c>list_regions</c> tool (docs/mcp-tools.md#list_regions, ADR 010).</summary>
/// <remarks>
/// The tool is registered with its contract name, description and <c>outputSchema</c>, but it is
/// not wired to the region catalog yet: the catalog is not seeded, and returning an empty list
/// would look like a working tool reporting "no regions exist". The call returns the missing
/// prerequisite instead (docs/adr/044-tools-ship-as-stubs-first.md).
/// </remarks>
[McpServerToolType]
public sealed class RegionTools
{
    /// <summary>Lists the regions this server can search.</summary>
    /// <param name="query">
    /// Optional substring filter over region id and label; absent or empty means all regions.
    /// </param>
    /// <param name="cancellationToken">The request's cancellation token; polled before doing anything.</param>
    /// <returns>
    /// The region list, or — in this build — a result explaining that the catalog is not seeded.
    /// </returns>
    [McpServerTool(Name = "list_regions", UseStructuredContent = true, OutputSchemaType = typeof(RegionListResponse))]
    [Description(
        "Lists the regions this server can search, each as {id, label, offers_count}. " +
        "Optional `query` filters by region id or label; absent or empty means all regions. " +
        "truncated=true means the list was cut and the query should be narrowed. " +
        "RULE: the value passed to search_tours.region MUST be an `id` returned by this tool — " +
        "never a label, never a name from memory. A wrong region produces a job that completes " +
        "with zero offers, which is indistinguishable from a genuinely empty result. " +
        "NOT IMPLEMENTED in this build: the region catalog is not seeded yet and this call " +
        "returns the missing prerequisite instead of a region list.")]
    public static CallToolResult ListRegions(
        [Description("Optional filter over region id and label (substring match). Absent means all regions.")]
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return StubResult.NotImplemented(
            "not implemented: 'list_regions' returned no region data. " +
            "Missing prerequisite: the region catalog is not seeded yet — it is seeded from the " +
            "region list in selectors.json, which does not exist in this repository yet " +
            "(docs/mcp-tools.md#list_regions, ADR 011). " +
            "No region list was invented: do not call search_tours with a region you guessed, " +
            "because a guessed region silently produces a job with zero offers.");
    }
}
