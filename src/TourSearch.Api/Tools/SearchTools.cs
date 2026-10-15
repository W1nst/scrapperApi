using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TourSearch.Api.Tools.Contracts;
using TourSearch.Core.Entities;
using TourSearch.Core.Enums;
using TourSearch.Core.Search;
using TourSearch.Infrastructure.Jobs;
using TourSearch.Infrastructure.Regions;

namespace TourSearch.Api.Tools;

/// <summary>
/// The <c>search_tours</c> and <c>get_search_results</c> tools
/// (docs/mcp-tools.md#search_tours, docs/mcp-tools.md#get_search_results).
/// </summary>
/// <remarks>
/// <para>
/// <c>search_tours</c> is <b>implemented</b>: it validates its parameters, canonicalises them into
/// <c>parameters_hash</c>, reuses a recent <c>Completed</c> job with the same hash, enforces the
/// global active-job limit, and creates a <b>Pending</b> job. It writes a row and returns — it
/// scrapes nothing, claims nothing and starts no work (ADR 005, ADR 038).
/// </para>
/// <para>
/// <c>get_search_results</c> is <b>still a stub</b>: reading a job is a separate task, and this
/// build has no worker, so there is nothing to read yet. It keeps the "NOT IMPLEMENTED" marker in
/// its description while the other two tools no longer carry it — the discriminator is per tool and
/// is asserted by a test (ADR 044).
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
    private const string Rub = "RUB";

    private readonly IJobRepository _jobs;
    private readonly IRegionRepository _regions;
    private readonly SearchOptions _options;

    /// <summary>Creates the tool over the repositories and settings of the current scope.</summary>
    /// <param name="jobs">The job queue's read/write path; scoped, like the context it wraps.</param>
    /// <param name="regions">The catalog reader, used to validate <c>region</c>.</param>
    /// <param name="options">The reuse window, the active-job limit and the poll hint.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public SearchTools(IJobRepository jobs, IRegionRepository regions, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(options);

        _jobs = jobs;
        _regions = regions;
        _options = options;
    }

    /// <summary>Validates the parameters, then starts — or reuses — one search job.</summary>
    /// <param name="region">The region id returned by <c>list_regions</c>.</param>
    /// <param name="adults">Number of adults; at least 1.</param>
    /// <param name="start_date">Desired check-in date, day precision.</param>
    /// <param name="nights">Number of nights; at least 1.</param>
    /// <param name="children">Number of children; 0 or more.</param>
    /// <param name="hotel_stars">Minimum hotel rating 1..5; 0 means any or unknown.</param>
    /// <param name="price_min">Lowest acceptable price for the stay; absent means unbounded.</param>
    /// <param name="price_max">Highest acceptable price for the stay; absent means unbounded.</param>
    /// <param name="currency">ISO currency code the prices are expressed in; <c>RUB</c> today.</param>
    /// <param name="cancellationToken">The request's cancellation token, forwarded to every query and write.</param>
    /// <returns>
    /// The started (or reused) job as <c>job_id</c>, <c>status</c>, <c>reused</c>,
    /// <c>parameters_hash</c> and <c>poll_after_ms</c>; or a tool-level error naming the field that
    /// is invalid, or <c>RATE_LIMITED</c> when the global limit is reached.
    /// </returns>
    /// <remarks>
    /// The order is the contract's: validate, canonicalise, reuse, count, insert. <b>Reuse is
    /// checked before the limit</b> — a reused job creates no work, so the cap it does not consume
    /// must never reject it (NFR-002).
    /// </remarks>
    [McpServerTool(Name = "search_tours", UseStructuredContent = true, OutputSchemaType = typeof(SearchStartedResponse))]
    [Description(
        "Starts a tour search for one region and returns immediately: this call is NON-BLOCKING " +
        "and never waits for results. The result carries job_id, status, reused, parameters_hash " +
        "and poll_after_ms; the caller must then poll get_search_results with that job_id and must " +
        "wait at least poll_after_ms before the first poll and between later polls. " +
        "region MUST be an id returned by list_regions — never invent one. " +
        "reused=true means an existing Completed job with identical parameters was returned " +
        "instead of a new one being started; only Completed jobs are reused, and only while they " +
        "are inside the 24-hour reuse window — an older one is not fresh enough to pass off as a " +
        "new answer. " +
        "A new job is created only while fewer than 10 jobs are active (Pending or InProgress): " +
        "that limit is GLOBAL, shared by every caller, because there is no per-caller identity yet, " +
        "and at the limit the call is refused with error_code RATE_LIMITED without creating a job; " +
        "a reused job does not count against it and is never blocked by it. " +
        "Every parameter is checked before anything is stored: an invalid one comes back as an " +
        "error result with isError=true and error_code PARAM_INVALID naming the offending field, " +
        "and no job is created. " +
        "NO WORKER EXISTS IN THIS BUILD: a created job stays Pending and is never picked up, so it " +
        "never produces offers — do not wait for a result that cannot arrive. get_search_results is " +
        "still a stub in this build and reports no status either.")]
    public async Task<CallToolResult> SearchTours(
        [Description("Region id returned by list_regions. A label or an invented name is wrong and is rejected.")]
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
        [Description("ISO currency code of the price bounds. Default RUB; only RUB is accepted today.")]
        string currency = Rub,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Validate everything before anything is written, so a rejected request leaves no row
        //    (FR-001). The rules live here and only here (ADR 038, NFR-001).
        if (await FindValidationFailureAsync(
                region,
                adults,
                children,
                hotel_stars,
                start_date,
                nights,
                price_min,
                price_max,
                currency,
                cancellationToken).ConfigureAwait(false) is { } failure)
        {
            return ToolErrors.ParamInvalid(failure.Field, failure.Message);
        }

        // 2. Canonicalise: the same logical request must yield the same hash in every process and
        //    every locale, or dedupe silently stops working (FR-002).
        var request = new SearchRequest(
            region, adults, children, hotel_stars, start_date, nights, price_min, price_max, currency);
        var parametersHash = ParameterHasher.ComputeHash(request);

        // 3. Reuse BEFORE the limit: a reused job creates no work, so the cap it does not consume
        //    must never be able to reject it (NFR-002).
        var reusable = await _jobs
            .FindReusableCompletedAsync(
                parametersHash,
                DateTime.UtcNow - _options.ReuseWindow,
                cancellationToken)
            .ConfigureAwait(false);

        if (reusable is not null)
        {
            return Started(reusable.Id, reusable.Status, reused: true, parametersHash);
        }

        // 4. The global active-job limit, computed by query — never a stored counter
        //    (docs/mcp-tools.md#rate-limiting).
        var activeJobs = await _jobs.CountActiveAsync(cancellationToken).ConfigureAwait(false);

        if (activeJobs >= _options.ActiveJobLimit)
        {
            return ToolErrors.RateLimited(
                $"RATE_LIMITED: {activeJobs} search jobs are already active (Pending or InProgress) " +
                $"and the limit is {_options.ActiveJobLimit}, so no job was created. The limit is " +
                "GLOBAL — it is shared by every caller, not per caller, because there is no " +
                "per-caller identity yet. Retry once the active jobs finish, or repeat an identical " +
                "request to reuse an existing Completed result, which does not count against it.");
        }

        // 5. Create the job unclaimed: Pending, attempt 0, no lease, no terminal timestamp. Claiming
        //    is the Worker's atomic compare-and-set (ADR 005) and doing it here would double-claim.
        var job = new SearchJob
        {
            Id = Guid.NewGuid(),
            ParametersHash = parametersHash,
            Parameters = request.ToParameters(),
            CurrencyCode = request.Currency,
            Status = JobStatus.Pending,
            Attempt = 0,
            CreatedAt = DateTime.UtcNow,
            Degraded = false,
            Warnings = [],
            OffersCount = 0,
            PagesDone = 0,
            PagesTotal = null,

            // Written out rather than left to the initialisers: these are the fields a claim would
            // set, and their being null is the contract, not an accident.
            LeaseToken = null,
            LeaseExpiresAt = null,
            StartedAt = null,
            CompletedAt = null,
            CancelRequestedAt = null,
            ErrorCode = null,
            ErrorMessage = null,
            Retryable = null,
        };

        await _jobs.CreateAsync(job, cancellationToken).ConfigureAwait(false);

        // 6. Exactly the contract's five fields.
        return Started(job.Id, job.Status, reused: false, parametersHash);
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

    /// <summary>Checks every parameter of a search and returns the first rule that fails.</summary>
    /// <param name="region">The requested region id.</param>
    /// <param name="adults">The requested number of adults.</param>
    /// <param name="children">The requested number of children.</param>
    /// <param name="hotelStars">The requested hotel rating floor.</param>
    /// <param name="startDate">The requested check-in date.</param>
    /// <param name="nights">The requested number of nights.</param>
    /// <param name="priceMin">The requested lower price bound, if any.</param>
    /// <param name="priceMax">The requested upper price bound, if any.</param>
    /// <param name="currency">The requested currency code.</param>
    /// <param name="cancellationToken">The request's token, forwarded to the catalog check.</param>
    /// <returns>The failing field and its rule, or <see langword="null"/> when every rule holds.</returns>
    /// <remarks>
    /// <para>
    /// The rules are the table in FR-001 and are checked in its order, so a request with several
    /// invalid fields fails deterministically on the first one.
    /// </para>
    /// <para>
    /// The <c>region</c> rule is the catalog's: a region that does not exist, or that the source
    /// stopped selling, is rejected here rather than becoming a job that completes with zero offers
    /// — a failure indistinguishable from a genuinely empty result (ADR 010).
    /// </para>
    /// </remarks>
    private async Task<ValidationFailure?> FindValidationFailureAsync(
        string region,
        int adults,
        int children,
        int hotelStars,
        DateOnly startDate,
        int nights,
        decimal? priceMin,
        decimal? priceMax,
        string currency,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            return new ValidationFailure(
                "region",
                "PARAM_INVALID: parameter 'region' is required. Pass an id returned by " +
                "list_regions — a label, a name or a numeric source id is not one.");
        }

        if (!await _regions.ExistsActiveAsync(region, cancellationToken).ConfigureAwait(false))
        {
            return new ValidationFailure(
                "region",
                $"PARAM_INVALID: parameter 'region' — '{region}' is not a region this server can " +
                "search. Call list_regions and pass one of the ids it returns; a label (for example " +
                "a Russian display name) and an invented name are both rejected.");
        }

        if (adults < 1)
        {
            return new ValidationFailure(
                "adults",
                "PARAM_INVALID: parameter 'adults' must be at least 1.");
        }

        if (children < 0)
        {
            return new ValidationFailure(
                "children",
                "PARAM_INVALID: parameter 'children' must be 0 or more.");
        }

        if (hotelStars is < 0 or > 5)
        {
            return new ValidationFailure(
                "hotel_stars",
                "PARAM_INVALID: parameter 'hotel_stars' must be between 0 and 5, where 0 means any " +
                "or unknown.");
        }

        if (startDate < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return new ValidationFailure(
                "start_date",
                $"PARAM_INVALID: parameter 'start_date' ({startDate:yyyy-MM-dd}) is in the past. " +
                "It is a check-in date, so it must be today's UTC date or later.");
        }

        if (nights < 1)
        {
            return new ValidationFailure(
                "nights",
                "PARAM_INVALID: parameter 'nights' must be at least 1.");
        }

        if (priceMin is < 0)
        {
            return new ValidationFailure(
                "price_min",
                "PARAM_INVALID: parameter 'price_min' must not be negative. Omit it for no lower " +
                "bound.");
        }

        if (priceMax is { } upperBound && priceMin is { } lowerBound && upperBound < lowerBound)
        {
            return new ValidationFailure(
                "price_max",
                $"PARAM_INVALID: parameter 'price_max' ({upperBound}) must be greater than or equal " +
                $"to 'price_min' ({lowerBound}).");
        }

        if (!string.Equals(currency, Rub, StringComparison.Ordinal))
        {
            return new ValidationFailure(
                "currency",
                $"PARAM_INVALID: parameter 'currency' — only '{Rub}' is supported, so " +
                $"'{currency}' was rejected rather than stored. Omit it to use the default.");
        }

        return null;
    }

    /// <summary>Builds the successful result: exactly the contract's five fields.</summary>
    /// <param name="jobId">The created or reused job's identifier.</param>
    /// <param name="status">The job's status: <c>Pending</c> for a new job, the reused job's own status otherwise.</param>
    /// <param name="reused">Whether an existing completed job was returned.</param>
    /// <param name="parametersHash">The canonical hash the job belongs to.</param>
    /// <returns>A normal result whose structured content is the response record.</returns>
    private CallToolResult Started(Guid jobId, JobStatus status, bool reused, string parametersHash)
    {
        var response = new SearchStartedResponse(jobId, status, reused, parametersHash, _options.PollAfterMs);

        // The SDK's own JSON options, so the payload uses the same naming, enum and encoding rules
        // as every other message this server emits (the enum must be the member name, "Pending").
        var payload = JsonSerializer.SerializeToElement(response, McpJsonUtilities.DefaultOptions);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = payload.GetRawText() }],
            StructuredContent = payload,
            IsError = false,
        };
    }

    /// <summary>One failed parameter rule: the field to fix and the explanation to read.</summary>
    /// <param name="Field">The contract's field name, as the caller wrote it in <c>inputSchema</c>.</param>
    /// <param name="Message">The rule that failed, naming the field and the accepted range.</param>
    private readonly record struct ValidationFailure(string Field, string Message);
}
