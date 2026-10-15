namespace TourSearch.Migrations;

/// <summary>
/// One region of the catalog a migration seeds: the slug the agent uses, the source's own id, the
/// label, and the parent's slug (<see langword="null"/> for a top-level region).
/// </summary>
/// <remarks>
/// This is the embedded seed the migration inserts — not a file it reads. A migration that read
/// <c>selectors.json</c> at run time would make an already-applied migration depend on the working
/// tree and stop being reproducible (ADR 046).
/// </remarks>
/// <param name="Id">Slug primary key, e.g. <c>"kemer"</c>.</param>
/// <param name="SourceId">The source's own id for the region, e.g. <c>"22"</c>.</param>
/// <param name="Label">The source's name for the region, verbatim.</param>
/// <param name="ParentId">The parent region's slug, or <see langword="null"/>.</param>
public sealed record RegionSeedRow(string Id, string SourceId, string Label, string? ParentId);
