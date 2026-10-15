using TourSearch.Core.Entities;

namespace TourSearch.Core.Search;

/// <summary>
/// The nine inputs of a <c>search_tours</c> call, <b>already validated</b>
/// (docs/mcp-tools.md#search_tours, docs/adr/038-parameter-validation-at-tool-layer.md).
/// </summary>
/// <remarks>
/// <para>
/// This type is what the tool layer produces once every rule of FR-001 holds, and it is what
/// <see cref="ParameterHasher"/> canonicalises. It carries no validation of its own: the ranges
/// are enforced once, at the MCP tool boundary (ADR 038), and a second copy of a rule here would
/// drift away from the first.
/// </para>
/// <para>
/// <see cref="Currency"/> is one of the nine inputs and belongs here, while the persisted
/// <c>SearchJob.CurrencyCode</c> keeps it out of <see cref="SearchParameters"/>: the composed
/// parameters have eight fields, and the currency is a property of the job's price band
/// (<see cref="ToParameters"/> deliberately drops it — the tool writes it to
/// <c>SearchJob.CurrencyCode</c>).
/// </para>
/// </remarks>
/// <param name="Region">A <see cref="Region.Id"/> slug the catalog returned, e.g. <c>"kemer"</c>.</param>
/// <param name="Adults">Number of adults; at least 1.</param>
/// <param name="Children">Number of children; 0 or more.</param>
/// <param name="HotelStars">Hotel rating 0..5; <c>0</c> means any/unknown.</param>
/// <param name="StartDate">Check-in date, day precision, in UTC terms.</param>
/// <param name="Nights">Number of nights; at least 1.</param>
/// <param name="PriceMin">Lower price bound, or <see langword="null"/> for no lower bound.</param>
/// <param name="PriceMax">Upper price bound, or <see langword="null"/> for no upper bound.</param>
/// <param name="Currency">ISO-4217 code the bounds are expressed in; <c>RUB</c> today.</param>
public sealed record SearchRequest(
    string Region,
    int Adults,
    int Children,
    int HotelStars,
    DateOnly StartDate,
    int Nights,
    decimal? PriceMin,
    decimal? PriceMax,
    string Currency)
{
    /// <summary>
    /// Projects the request onto the eight fields that are persisted inside
    /// <see cref="SearchJob.Parameters"/>.
    /// </summary>
    /// <returns>
    /// The parameters of the job, with <see cref="SearchParameters.StartDate"/> at day precision
    /// with <see cref="DateTimeKind.Utc"/> (docs/database.md#utc-trap) and absent price bounds
    /// preserved as <see langword="null"/>.
    /// </returns>
    public SearchParameters ToParameters() => new()
    {
        Region = Region,
        Adults = Adults,
        Children = Children,
        HotelStars = HotelStars,

        // Midnight UTC, day precision by convention: a date-only value has no wall-clock time, and
        // an Unspecified kind is exactly the value Npgsql refuses (docs/domain.md#date-types).
        StartDate = StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        Nights = Nights,
        PriceMin = PriceMin,
        PriceMax = PriceMax,
    };
}
