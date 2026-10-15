namespace TourSearch.Infrastructure.Regions;

/// <summary>
/// Reads the region catalog that backs <c>list_regions</c> — the only source of valid region ids
/// for <c>search_tours</c> (docs/mcp-tools.md#list_regions, ADR 010).
/// </summary>
/// <remarks>
/// <para>
/// The catalog is seeded by migration, never by application startup (ADR 041, ADR 046), so this
/// interface is read-only: nothing in the API process writes a region.
/// </para>
/// <para>
/// An in-memory implementation of this interface exists in the unit tests only. It is what lets the
/// tool's response shape be asserted without a database; the SQL semantics below are verified
/// against a real PostgreSQL by the task that implements them.
/// </para>
/// </remarks>
public interface IRegionRepository
{
    /// <summary>
    /// Lists the regions the server can search, each with the number of offers currently known
    /// for it.
    /// </summary>
    /// <param name="query">
    /// Optional case-insensitive substring filter over the region id and the region label. Absent,
    /// empty or whitespace means "all regions" — a whitespace query is not a filter that matches
    /// nothing. The percent and underscore characters are literal text, not wildcards.
    /// </param>
    /// <param name="cancellationToken">The request's cancellation token, forwarded to the query.</param>
    /// <returns>
    /// The matching regions, ordered by <see cref="RegionOffersCount.Label"/> ascending with the id
    /// as the tiebreak, so repeated calls return the same order. Regions the source stopped selling
    /// (<c>Active = false</c>) are not returned. Empty when nothing matches — a valid result, not
    /// an error.
    /// </returns>
    Task<IReadOnlyList<RegionOffersCount>> ListActiveAsync(string? query, CancellationToken cancellationToken);
}
