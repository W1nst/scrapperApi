namespace TourSearch.Core.Entities;

/// <summary>
/// One tour offer found by a <see cref="SearchJob"/>, restricted to the fields the
/// caller is allowed to see.
/// </summary>
/// <remarks>
/// The untouched scraped payload (<c>Raw</c>/<c>Jsonb</c>) is deliberately absent: it is
/// <c>[PLANNED]</c> in docs/domain.md and nothing writes it yet. All persistence
/// configuration lives in <c>TourSearch.Infrastructure</c>.
/// </remarks>
public class TourOffer
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>The job that produced this offer. Deleting the job deletes the offer.</summary>
    public Guid SearchJobId { get; set; }

    /// <summary>Hotel name as shown by the source.</summary>
    public string HotelName { get; set; } = "";

    /// <summary>Hotel rating 1..5; <c>0</c> means unknown — see docs/domain.md#hotelstars.</summary>
    public int HotelStars { get; set; }

    /// <summary>The source's "5+"/deluxe band: <c>HotelStars = 5</c> plus this flag.</summary>
    public bool Luxury { get; set; }

    /// <summary>
    /// Foreign key to <see cref="Region.Id"/>. A region is deactivated, never deleted, so
    /// the relationship restricts deletes instead of cascading — see docs/domain.md#region-catalog.
    /// </summary>
    public string RegionId { get; set; } = "";

    /// <summary>UTC, day precision by convention — see docs/domain.md#date-types.</summary>
    public DateTime CheckIn { get; set; }

    /// <summary>Number of nights.</summary>
    public int Nights { get; set; }

    /// <summary>Price for the whole stay.</summary>
    public decimal Price { get; set; }

    /// <summary>ISO-4217 code of <see cref="Price"/>. docs/domain.md#currency: RUB for now.</summary>
    public string CurrencyCode { get; set; } = "RUB";

    /// <summary>Room type as shown by the source, when it is shown at all.</summary>
    public string? RoomType { get; set; }

    /// <summary>Normalized meal code (<c>AI</c>, <c>BB</c>, <c>HB</c>) when it could be derived.</summary>
    public string? MealType { get; set; }

    /// <summary>Where the offer was seen — also the within-job dedupe key.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>
    /// UTC. When the offer was scraped — the ordering column of the offer pagination
    /// cursor, used together with <see cref="Id"/> (docs/domain.md#guid-ordering).
    /// </summary>
    public DateTime ScrapedAt { get; set; }
}
