using Microsoft.EntityFrameworkCore;
using TourSearch.Core.Entities;

namespace TourSearch.Infrastructure.Regions;

/// <summary>
/// Reads the region catalog from the one <see cref="TourSearchDbContext"/>, with the offer count
/// computed in the database.
/// </summary>
/// <remarks>
/// <para>
/// Registered <c>Scoped</c>: it resolves the request's context and holds no state of its own
/// (docs/conventions.md#di). It reads only — regions are seeded by migration (ADR 046).
/// </para>
/// <para>
/// The catalog and its offer counts are read in <b>one</b> statement: the regions are LEFT JOINed
/// onto a single grouped count of <c>tour_offers</c>, never one count query per region (NFR-002).
/// The join is served by <c>ix_tour_offers_region_id</c> (docs/database.md#index-rules).
/// </para>
/// </remarks>
public sealed class RegionRepository : IRegionRepository
{
    private readonly TourSearchDbContext _context;

    /// <summary>Creates the repository over the context of the current scope.</summary>
    /// <param name="context">The scope's <see cref="TourSearchDbContext"/>; never disposed here.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public RegionRepository(TourSearchDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegionOffersCount>> ListActiveAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        // Plain `await`, no ConfigureAwait(false): nothing here runs on a UI thread and the
        // request's CancellationToken must reach the query unchanged (docs/conventions.md#cancellation).
        return await BuildListQuery(query)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A single existence check on the primary key, with the <c>active</c> predicate applied in the
    /// database: no catalog row is materialised, and an inactive region answers
    /// <see langword="false"/> exactly like an unknown one (FR-001).
    /// </remarks>
    public async Task<bool> ExistsActiveAsync(string regionId, CancellationToken cancellationToken)
    {
        return await _context.Regions
            .AsNoTracking()
            .AnyAsync(region => region.Id == regionId && region.Active, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the SQL that <see cref="ListActiveAsync"/> runs for <paramref name="query"/>, without
    /// running it.
    /// </summary>
    /// <param name="query">The same filter <see cref="ListActiveAsync"/> accepts.</param>
    /// <returns>The statement the query translates to.</returns>
    /// <remarks>
    /// Building a command opens no connection, so this is how a test asserts the <b>shape</b> of the
    /// read — one statement, one grouped count, an <c>active</c> predicate, a case-insensitive
    /// substring filter and a deterministic order — without a database (NFR-002,
    /// docs/conventions.md#testing).
    /// </remarks>
    public string BuildListStatement(string? query) => BuildListQuery(query).ToQueryString();

    /// <summary>
    /// Builds the catalog read: active regions, the optional filter, the grouped offer counts and
    /// the deterministic order.
    /// </summary>
    /// <param name="query">The raw filter, or <see langword="null"/> for the whole catalog.</param>
    /// <returns>The composed query, executed by <see cref="ListActiveAsync"/>.</returns>
    private IQueryable<RegionOffersCount> BuildListQuery(string? query)
    {
        // The count per region, grouped once for the whole catalog. Npgsql materializes this as a
        // `GROUP BY region_id` subquery, so the join below is a single statement. The count is
        // projected as nullable on purpose: a region without offers is a NULL row of the LEFT JOIN,
        // and a non-nullable projection would make the materializer throw "Nullable object must have
        // a value" on exactly the regions that have no offers yet (verified).
        var offersPerRegion = _context.TourOffers
            .AsNoTracking()
            .GroupBy(offer => offer.RegionId)
            .Select(group => new { RegionId = group.Key, Count = (int?)group.Count() });

        // A region the source stopped selling must not be offered to the agent (FR-005).
        var regions = _context.Regions
            .AsNoTracking()
            .Where(region => region.Active);

        // Whitespace is "absent", not a filter that matches nothing. `ToLower()` + `Contains()`
        // becomes lower(...) LIKE @pattern, and the translated pattern escapes '%' and '_' —
        // verified: the query "50%_x" arrives as '%50\%\_x%'. So the wildcard characters are
        // literal text, which a hand-written LIKE would have made a wildcard (task edge cases).
        var text = query?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var lowered = text.ToLowerInvariant();
            regions = regions.Where(region =>
                region.Id.ToLower().Contains(lowered) || region.Label.ToLower().Contains(lowered));
        }

        // Deterministic order so repeated calls are stable; a region without offers still gets a
        // row, with a count of 0 rather than a missing field.
        return from region in regions
               join offers in offersPerRegion on region.Id equals offers.RegionId into regionOffers
               from regionOffer in regionOffers.DefaultIfEmpty()
               orderby region.Label, region.Id
               select new RegionOffersCount(
                   region.Id,
                   region.Label,
                   regionOffer == null ? 0 : regionOffer.Count ?? 0);
    }
}
