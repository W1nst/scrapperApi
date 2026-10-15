using System.ComponentModel;
using ModelContextProtocol.Server;
using TourSearch.Api.Tools.Contracts;
using TourSearch.Infrastructure.Regions;

namespace TourSearch.Api.Tools;

/// <summary>The <c>list_regions</c> tool (docs/mcp-tools.md#list_regions, ADR 010).</summary>
/// <remarks>
/// <para>
/// This is the catalog's read path: it returns the regions the server can search, each with the
/// number of offers currently known for it. It reads the seeded catalog from the database — it
/// never calls the source, never invents a region, and never fetches <c>selectors.json</c> at
/// runtime (ADR 046).
/// </para>
/// <para>
/// The instance is built per call and takes its dependencies from the request's scope, so the
/// repository and the context it wraps live exactly as long as the call
/// (docs/conventions.md#di).
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class RegionTools
{
    private readonly IRegionRepository _regions;

    /// <summary>Creates the tool over the region catalog reader of the current scope.</summary>
    /// <param name="regions">The catalog reader; scoped, like the context it wraps.</param>
    /// <exception cref="ArgumentNullException"><paramref name="regions"/> is <see langword="null"/>.</exception>
    public RegionTools(IRegionRepository regions)
    {
        ArgumentNullException.ThrowIfNull(regions);

        _regions = regions;
    }

    /// <summary>Lists the regions this server can search.</summary>
    /// <param name="query">
    /// Optional case-insensitive substring filter over region id and label; absent, empty or
    /// whitespace means all regions.
    /// </param>
    /// <param name="cancellationToken">
    /// The request's cancellation token, forwarded to the query. A cancelled call returns nothing
    /// and changes nothing.
    /// </param>
    /// <returns>The regions matching the filter, with their computed offer counts.</returns>
    /// <remarks>
    /// A failing read is deliberately not caught: an empty list would be indistinguishable from
    /// "no region exists", which is the confusion ADR 010 exists to prevent. The exception surfaces
    /// as a tool error instead.
    /// </remarks>
    [McpServerTool(Name = "list_regions", UseStructuredContent = true, OutputSchemaType = typeof(RegionListResponse))]
    [Description(
        "Lists the regions this server can search, each as {id, label, offers_count}. " +
        "Optional `query` filters by region id or label; absent or empty means all regions. " +
        "truncated=true means the list was cut and the query should be narrowed. " +
        "RULE: the value passed to search_tours.region MUST be an `id` returned by this tool — " +
        "never a label, never a name from memory. A wrong region produces a job that completes " +
        "with zero offers, which is indistinguishable from a genuinely empty result. " +
        "offers_count is the number of offers the server currently knows for a region, computed " +
        "from the stored offers; 0 means none are stored yet, not that the region is unavailable.")]
    public async Task<RegionListResponse> ListRegions(
        [Description("Optional filter over region id and label (substring match). Absent means all regions.")]
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        // Whitespace is "absent", not a filter that matches nothing: the repository documents the
        // same rule, and the tool applies it up front so the contract is explicit at the edge.
        var filter = string.IsNullOrWhiteSpace(query) ? null : query.Trim();

        var regions = await _regions
            .ListActiveAsync(filter, cancellationToken)
            .ConfigureAwait(false);

        return new RegionListResponse(
            regions.Select(region => new RegionSummary(region.Id, region.Label, region.OffersCount)).ToList(),

            // No cap is applied to the catalog today: the whole active catalog is returned, and the
            // field stays in the contract so a future cap is not a breaking change. When a cap is
            // added this must report it honestly, or the agent reads a cut list as "the region does
            // not exist" (ADR 010).
            Truncated: false);
    }
}
