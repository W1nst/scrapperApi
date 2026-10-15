using System.Text.Json.Serialization;

namespace TourSearch.Api.Tools.Contracts;

/// <summary>
/// The result of <c>list_regions</c> (docs/mcp-tools.md#list_regions).
/// </summary>
/// <remarks>
/// This type is the tool's declared <c>outputSchema</c>. Property names are snake_case because
/// the MCP contract is snake_case while the SDK serializes camelCase by default; the
/// <c>[property: JsonPropertyName]</c> annotations are what make the two agree — verified
/// against the emitted schema.
/// </remarks>
/// <param name="Regions">The regions the server can search, empty when none match.</param>
/// <param name="Truncated">
/// <see langword="true"/> when the list was cut short, so the agent knows to narrow its query
/// instead of concluding that a region does not exist (ADR 010).
/// </param>
public sealed record RegionListResponse(
    [property: JsonPropertyName("regions")] IReadOnlyList<RegionSummary> Regions,
    [property: JsonPropertyName("truncated")] bool Truncated);

/// <summary>One region of <see cref="RegionListResponse"/>.</summary>
/// <param name="Id">
/// The region slug — the only value <c>search_tours.region</c> accepts, and a foreign key in the
/// database (docs/domain.md#region-catalog).
/// </param>
/// <param name="Label">The display name of the region.</param>
/// <param name="OffersCount">
/// Offers currently known for the region. Computed from the offers table, never a stored
/// counter.
/// </param>
public sealed record RegionSummary(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("offers_count")] int OffersCount);
