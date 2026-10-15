using System.Text.Json.Serialization;
using TourSearch.Core.Enums;

namespace TourSearch.Api.Tools.Contracts;

/// <summary>
/// The result of <c>get_search_results</c> (docs/mcp-tools.md#get_search_results).
/// </summary>
/// <remarks>
/// <para>
/// One tool, one schema, four documented shapes — not ready, ready, empty-but-valid, and failed.
/// The contract's examples therefore model as a single record whose fields are all optional
/// except <see cref="Status"/>; which fields are present is decided by the status, exactly as the
/// contract's examples show:
/// </para>
/// <list type="bullet">
/// <item><description><c>Pending</c>/<c>InProgress</c>: <c>status</c>, <c>poll_after_ms</c>, <c>progress</c>.</description></item>
/// <item><description><c>Completed</c>: <c>status</c>, <c>offers</c>, <c>page</c>, <c>degraded</c>, <c>warnings</c>, <c>offers_total</c>, <c>scraped_at</c>.</description></item>
/// <item><description><c>Failed</c>: <c>status</c>, <c>error_code</c>, <c>error_message</c>, <c>retryable</c>, <c>attempt</c>.</description></item>
/// </list>
/// <para>
/// A job that is not yours and a job that does not exist are the same answer on purpose: telling
/// them apart is an information leak (docs/mcp-tools.md#errors--two-different-layers).
/// </para>
/// </remarks>
/// <param name="Status">The status of the job; always present.</param>
/// <param name="PollAfterMs">The delay the caller must wait before polling again.</param>
/// <param name="Progress">Running counters while the job is not terminal.</param>
/// <param name="Offers">One page of offers; never the whole result set (ADR 008).</param>
/// <param name="Page">The cursor of this page and whether more follows.</param>
/// <param name="Degraded">
/// <see langword="true"/> when the job completed with partial results — the job is
/// <c>Completed</c>, not <c>Failed</c> (ADR 006).
/// </param>
/// <param name="Warnings">Non-fatal parse problems reported by the worker.</param>
/// <param name="OffersTotal">The single authoritative offer count — there is no second count inside <paramref name="Page"/>.</param>
/// <param name="ScrapedAt">When the offers were scraped. Always UTC.</param>
/// <param name="ErrorCode">Machine-readable failure code, from the error taxonomy (docs/domain.md#error-taxonomy).</param>
/// <param name="ErrorMessage">Human-readable failure message, for the agent and the operator.</param>
/// <param name="Retryable">Whether the failure is transient (ADR 007).</param>
/// <param name="Attempt">How many times a worker has claimed the job.</param>
public sealed record SearchResultsResponse(
    [property: JsonPropertyName("status")] JobStatus Status,
    [property: JsonPropertyName("poll_after_ms")] int? PollAfterMs,
    [property: JsonPropertyName("progress")] SearchProgress? Progress,
    [property: JsonPropertyName("offers")] IReadOnlyList<TourOfferSummary>? Offers,
    [property: JsonPropertyName("page")] OfferPage? Page,
    [property: JsonPropertyName("degraded")] bool? Degraded,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string>? Warnings,
    [property: JsonPropertyName("offers_total")] int? OffersTotal,
    [property: JsonPropertyName("scraped_at")] DateTime? ScrapedAt,
    [property: JsonPropertyName("error_code")] JobErrorCode? ErrorCode,
    [property: JsonPropertyName("error_message")] string? ErrorMessage,
    [property: JsonPropertyName("retryable")] bool? Retryable,
    [property: JsonPropertyName("attempt")] int? Attempt);

/// <summary>Running counters of a job that is not terminal yet.</summary>
/// <param name="OffersSoFar">Offers persisted so far.</param>
/// <param name="PagesDone">Pages parsed successfully.</param>
/// <param name="PagesTotal">Expected page count, or <see langword="null"/> when the source reports none.</param>
public sealed record SearchProgress(
    [property: JsonPropertyName("offers_so_far")] int OffersSoFar,
    [property: JsonPropertyName("pages_done")] int PagesDone,
    [property: JsonPropertyName("pages_total")] int? PagesTotal);

/// <summary>The cursor of one page of offers (ADR 008, ADR 009).</summary>
/// <param name="NextCursor">The opaque cursor for the next page, or <see langword="null"/> at the end.</param>
/// <param name="HasMore">Whether more offers follow.</param>
public sealed record OfferPage(
    [property: JsonPropertyName("next_cursor")] string? NextCursor,
    [property: JsonPropertyName("has_more")] bool HasMore);

/// <summary>One offer as the agent sees it (docs/mcp-tools.md#get_search_results).</summary>
/// <param name="HotelName">Name of the hotel as the source reports it.</param>
/// <param name="HotelStars">Rating 1..5; 0 means unknown, and the source's "5+" band is 5 with luxury set.</param>
/// <param name="Luxury">Whether the offer is in the source's luxury/deluxe band.</param>
/// <param name="Region">Region id — a foreign key to the region catalog, not a free-text name.</param>
/// <param name="CheckIn">Check-in date, day precision, from a UTC date.</param>
/// <param name="Nights">Number of nights.</param>
/// <param name="Price">Price for the whole stay, in <paramref name="Currency"/>.</param>
/// <param name="Currency">ISO currency code of the price.</param>
/// <param name="RoomType">Room type, when the source reports one.</param>
/// <param name="MealType">Normalized meal code (for example <c>AI</c>, <c>BB</c>, <c>HB</c>).</param>
/// <param name="SourceUrl">Link to the source page of the offer.</param>
public sealed record TourOfferSummary(
    [property: JsonPropertyName("hotel_name")] string HotelName,
    [property: JsonPropertyName("hotel_stars")] int HotelStars,
    [property: JsonPropertyName("luxury")] bool Luxury,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("check_in")] DateOnly CheckIn,
    [property: JsonPropertyName("nights")] int Nights,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("room_type")] string? RoomType,
    [property: JsonPropertyName("meal_type")] string? MealType,
    [property: JsonPropertyName("source_url")] string? SourceUrl);
