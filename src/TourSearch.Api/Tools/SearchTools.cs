using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TourSearch.Api.Tools.Contracts;

namespace TourSearch.Api.Tools;

/// <summary>
/// The <c>search_tours</c> and <c>get_search_results</c> tools
/// (docs/mcp-tools.md#search_tours, docs/mcp-tools.md#get_search_results).
/// </summary>
/// <remarks>
/// <para>
/// Both tools are registered with their contract names, descriptions, input schemas and
/// <c>outputSchema</c>s, but neither is wired to the database yet:
/// <c>search_tours</c> creates no job and <c>get_search_results</c> queries nothing, so both
/// return the missing prerequisite rather than a fabricated result
/// (docs/adr/044-tools-ship-as-stubs-first.md).
/// </para>
/// <para>
/// <b>Why these parameters are snake_case</b>, against the C# naming convention: the SDK takes
/// the JSON argument names of a tool — and therefore the property names in <c>inputSchema</c> —
/// from the C# parameter names, and <c>JsonPropertyName</c> cannot be applied to a parameter
/// (verified: it is valid only on properties, indexes and fields, and a naming policy on the
/// serializer does not rename parameters). Naming them as the contract does is the only way the
/// advertised schema and the accepted arguments agree; a camelCase schema would silently ignore
/// the contract's <c>hotel_stars</c>/<c>start_date</c> style arguments.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class SearchTools
{
    /// <summary>Starts a tour search and returns immediately.</summary>
    /// <param name="region">The region id returned by <c>list_regions</c>.</param>
    /// <param name="adults">Number of adults; at least 1.</param>
    /// <param name="start_date">Desired check-in date, day precision.</param>
    /// <param name="nights">Number of nights; at least 1.</param>
    /// <param name="children">Number of children; 0 or more.</param>
    /// <param name="hotel_stars">Minimum hotel rating 1..5; 0 means any or unknown.</param>
    /// <param name="price_min">Lowest acceptable price for the stay; absent means unbounded.</param>
    /// <param name="price_max">Highest acceptable price for the stay; absent means unbounded.</param>
    /// <param name="currency">ISO currency code the prices are expressed in.</param>
    /// <param name="cancellationToken">The request's cancellation token; polled before doing anything.</param>
    /// <returns>
    /// The started (or reused) job, or — in this build — a result stating that no job was created.
    /// </returns>
    [McpServerTool(Name = "search_tours", UseStructuredContent = true, OutputSchemaType = typeof(SearchStartedResponse))]
    [Description(
        "Starts a tour search for one region and returns immediately: this call is NON-BLOCKING " +
        "and never waits for results. The result carries job_id, status, reused, parameters_hash " +
        "and poll_after_ms; the caller must then poll get_search_results with that job_id and must " +
        "wait at least poll_after_ms before the first poll and between later polls. " +
        "region MUST be an id returned by list_regions — never invent one. " +
        "reused=true means an existing completed job with identical parameters was returned " +
        "instead of a new one being started. " +
        "NOT IMPLEMENTED in this build: this call starts no search, creates no job and returns no " +
        "job_id — it returns the missing prerequisite instead.")]
    public static CallToolResult SearchTours(
        [Description("Region id returned by list_regions. A label or an invented name is wrong and silently returns zero offers.")]
        string region,
        [Description("Number of adults; at least 1.")]
        int adults,
        [Description("Desired check-in date, day precision, ISO-8601 (for example 2026-06-15).")]
        DateOnly start_date,
        [Description("Number of nights; at least 1.")]
        int nights,
        [Description("Number of children; 0 or more. Default 0.")]
        int children = 0,
        [Description("Minimum hotel rating 1..5; 0 (default) means any or unknown.")]
        int hotel_stars = 0,
        [Description("Lowest acceptable price for the whole stay, in `currency`. Absent means no lower bound.")]
        decimal? price_min = null,
        [Description("Highest acceptable price for the whole stay, in `currency`. Absent means no upper bound.")]
        decimal? price_max = null,
        [Description("ISO currency code of the price bounds. Default RUB.")]
        string currency = "RUB",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return StubResult.NotImplemented(
            "not implemented: 'search_tours' did not start a search and created no job. " +
            "Missing prerequisite: job creation does not exist yet — the API writes no row and " +
            "touches no database (docs/adr/044-tools-ship-as-stubs-first.md). " +
            "There is no job_id to poll: do not call get_search_results for this request, and do " +
            "not treat this answer as a job status.");
    }

    /// <summary>Returns one page of the state of a search job.</summary>
    /// <param name="job_id">The job identifier returned by <c>search_tours</c>.</param>
    /// <param name="limit">Maximum number of offers in this page; default 20, maximum 50.</param>
    /// <param name="cursor">The opaque <c>next_cursor</c> of the previous page; absent means the first page.</param>
    /// <param name="cancellationToken">The request's cancellation token; polled before doing anything.</param>
    /// <returns>
    /// The job state and one page of offers, or — in this build — a result stating that nothing
    /// was queried.
    /// </returns>
    [McpServerTool(Name = "get_search_results", UseStructuredContent = true, OutputSchemaType = typeof(SearchResultsResponse))]
    [Description(
        "Returns the state of one search job and one PAGE of its offers. Ordered so the caller can " +
        "stop early. The shape of the result follows status: while Pending/InProgress it carries " +
        "poll_after_ms and progress{offers_so_far, pages_done, pages_total}; when Completed it " +
        "carries offers[], page{next_cursor, has_more}, offers_total, degraded, warnings[] and " +
        "scraped_at; when Failed it carries error_code, error_message, retryable and attempt. " +
        "Polling: call it with the job_id from search_tours, then wait poll_after_ms before the " +
        "next call — never poll tighter. Pagination: at most limit offers per call (default 20, " +
        "maximum 50) and follow next_cursor while has_more is true; the whole result set is never " +
        "returned at once. " +
        "NOT IMPLEMENTED in this build: this call queries no database and returns no offers and no " +
        "status — it returns the missing prerequisite instead.")]
    public static CallToolResult GetSearchResults(
        [Description("Job identifier returned by search_tours.")]
        Guid job_id,
        [Description("Maximum number of offers to return. Default 20, maximum 50.")]
        int limit = 20,
        [Description("Opaque cursor from the previous page's next_cursor. Absent means the first page.")]
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return StubResult.NotImplemented(
            "not implemented: 'get_search_results' queried no database and returned no job status " +
            "and no offers. Missing prerequisite: job lookup and offer pagination do not exist yet " +
            "(docs/adr/044-tools-ship-as-stubs-first.md). " +
            "This is not an empty result set and not a job status: treat it as 'not available yet'.");
    }
}
