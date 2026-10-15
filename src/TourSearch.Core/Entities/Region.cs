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
    /// <summary>
    /// Longest accepted <see cref="SourceId"/>, in characters. The database column is
    /// <c>varchar(16)</c> and <c>selectors.json</c> is validated against the same width, so the
    /// three agree by construction rather than by three copies of the number 16.
    /// </summary>
    public const int SourceIdMaxLength = 16;

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

    /// <summary>
    /// The source's own identifier for this region, as a numeric string, e.g. <c>"22"</c> for
    /// Kemer. It is what the parser and the scraper put into the source's request, so it must
    /// round-trip to the source verbatim — see docs/scraping.md#selectors.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="Id"/>: the agent sends back and reads
    /// <see cref="Id"/>, and a bare <c>"22"</c> in a log or a tool call is indistinguishable from
    /// a typo. It is also deliberately not <see cref="Source"/>, which means "which source this
    /// region belongs to" (<c>"tourvisor"</c>), not "which id that source uses".
    /// </remarks>
    public string SourceId { get; set; } = "";

    /// <summary>
    /// The parent region's <see cref="Id"/> slug, e.g. <c>"alanya"</c>; <see langword="null"/>
    /// for a top-level region.
    /// </summary>
    /// <remarks>
    /// The slug, not the source's numeric <c>parentid</c>, so this is a self-referencing foreign
    /// key to <see cref="Id"/> and stays meaningful if the source's numbering changes. A parent is
    /// deactivated, never deleted, so the foreign key restricts deletes (ADR 039).
    /// </remarks>
    public string? ParentId { get; set; }
}
