namespace TourSearch.Core.Entities;

/// <summary>
/// A destination the source sells, as exposed by <c>list_regions</c> and accepted by
/// <c>search_tours.region</c>. Regions are a catalog entity, not text derived from scraped
/// offers on demand — see docs/domain.md#region-catalog.
/// </summary>
/// <remarks>
/// A region the source stopped selling is deactivated (<see cref="Active"/> = <c>false</c>),
/// never deleted: offers reference it, and deleting one would take every offer of that
/// region with it.
/// </remarks>
public class Region
{
    /// <summary>Slug primary key, e.g. <c>"antalya"</c> — the value the caller passes back.</summary>
    public string Id { get; set; } = "";

    /// <summary>Display name, unbounded.</summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Source this region belongs to. One source per milestone today; kept so a second
    /// source is a data change rather than a schema change.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>Whether the source currently sells this region.</summary>
    public bool Active { get; set; }
}
