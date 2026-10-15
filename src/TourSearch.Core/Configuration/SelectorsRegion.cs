namespace TourSearch.Core.Configuration;

/// <summary>
/// One region of the <c>regions</c> section of <c>selectors.json</c>: the committed, human-editable
/// half of the region catalog.
/// </summary>
/// <remarks>
/// The other half is the seeded <c>regions</c> table, and a test asserts the two agree (NFR-001).
/// This type is the file's shape only — the catalog the tools read is the table (ADR 046).
/// </remarks>
/// <param name="Id">
/// The region slug, e.g. <c>"kemer"</c> — what <c>list_regions</c> returns and
/// <c>search_tours.region</c> accepts. Lowercase ASCII, hyphen separated.
/// </param>
/// <param name="SourceId">
/// The source's own numeric id for the region, e.g. <c>"22"</c>. It is the value the source's
/// request template needs, which is why the file carries it next to the slug.
/// </param>
/// <param name="Label">The source's name for the region, verbatim, in the source's own language.</param>
/// <param name="ParentId">
/// The slug of the parent region, or <see langword="null"/> for a top-level region. Deliberately a
/// slug and not the source's <c>parentid</c>, so it names a row this file also defines.
/// </param>
public sealed record SelectorsRegion(string Id, string SourceId, string Label, string? ParentId);
