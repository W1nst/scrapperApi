namespace TourSearch.Infrastructure.Regions;

/// <summary>
/// One row of the region catalog as <c>list_regions</c> needs it: the region's identity, its
/// display name, and the number of offers currently known for it.
/// </summary>
/// <remarks>
/// The count is <b>computed</b> from <c>tour_offers</c> on every call, never read from a stored
/// counter, so it cannot drift (docs/domain.md#region-catalog, ADR 010). A region with no offers
/// is reported with <see cref="OffersCount"/> = 0 rather than being omitted.
/// </remarks>
/// <param name="Id">The region slug — the value the agent passes back as <c>search_tours.region</c>.</param>
/// <param name="Label">The display name of the region, in the source's own wording.</param>
/// <param name="OffersCount">Offers currently known for the region; 0 when it has none.</param>
public sealed record RegionOffersCount(string Id, string Label, int OffersCount);
