namespace TourSearch.Core.Entities;

/// <summary>
/// The parameters a <see cref="SearchJob"/> was requested with.
/// </summary>
/// <remarks>
/// Persisted as an EF Core 10 complex type mapped to a single <c>jsonb</c> column named
/// <c>parameters</c> on <c>search_jobs</c> — not as a table of its own (docs/database.md).
/// A complex type has no identity, so it cannot be queried independently: lookups go
/// through <see cref="SearchJob.ParametersHash"/> instead.
///
/// The value ranges documented in docs/domain.md (Adults &gt;= 1, HotelStars 0..5,
/// PriceMax &gt;= PriceMin, ...) are deliberately **not** enforced here and not in the
/// database: the MCP tool layer rejects them with <c>PARAM_INVALID</c> before a job exists.
/// </remarks>
public class SearchParameters
{
    /// <summary>
    /// A <see cref="Region.Id"/> slug as returned by <c>list_regions</c>, e.g. <c>"antalya"</c>.
    /// </summary>
    public string Region { get; set; } = "";

    /// <summary>Number of adults.</summary>
    public int Adults { get; set; }

    /// <summary>Number of children.</summary>
    public int Children { get; set; }

    /// <summary>Hotel rating 1..5; <c>0</c> means any/unknown — see docs/domain.md#hotelstars.</summary>
    public int HotelStars { get; set; }

    /// <summary>UTC, day precision by convention — see docs/domain.md#date-types.</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Number of nights.</summary>
    public int Nights { get; set; }

    /// <summary>Lower bound of the price band.</summary>
    public decimal PriceMin { get; set; }

    /// <summary>Upper bound of the price band.</summary>
    public decimal PriceMax { get; set; }
}
