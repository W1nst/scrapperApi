using System.Text.Json;
using ModelContextProtocol.Protocol;
using TourSearch.Api;
using TourSearch.Api.Tools;
using TourSearch.Core.Entities;
using TourSearch.Core.Search;
using TourSearch.Infrastructure.Jobs;
using TourSearch.Infrastructure.Regions;

namespace TourSearch.UnitTests.Search;

/// <summary>
/// The request the search tests start from, and the tool call that submits it.
/// </summary>
/// <remarks>
/// One valid request as the baseline, so every test mutates exactly the parameter it is about and a
/// failure names that parameter rather than "the request was wrong".
/// </remarks>
internal static class SearchFixtures
{
    /// <summary>Today's date in the terms the tool validates against — day precision, UTC.</summary>
    internal static DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>
    /// A request that passes every rule of FR-001: a real catalog region, 2 adults, no children,
    /// 4 stars, a future check-in, 7 nights, a sane price band and the only supported currency.
    /// </summary>
    internal static SearchRequest ValidRequest() =>
        new(
            Region: "kemer",
            Adults: 2,
            Children: 0,
            HotelStars: 4,
            StartDate: TodayUtc.AddDays(30),
            Nights: 7,
            PriceMin: 500m,
            PriceMax: 1500m,
            Currency: "RUB");

    /// <summary>The documented defaults: a 24-hour reuse window, a limit of 10, a 5-second poll hint.</summary>
    internal static SearchOptions DefaultOptions() =>
        new(TimeSpan.FromHours(24), SearchOptions.DefaultActiveJobLimit, SearchOptions.DefaultPollAfterMs);

    /// <summary>
    /// Calls the tool the way the SDK does: argument by argument, with every snake_case name the
    /// caller would use.
    /// </summary>
    /// <param name="tool">The tool under test.</param>
    /// <param name="request">The request to submit.</param>
    /// <param name="cancellationToken">The token to pass through.</param>
    /// <returns>The tool's result, never thrown.</returns>
    internal static Task<CallToolResult> CallAsync(
        SearchTools tool,
        SearchRequest request,
        CancellationToken cancellationToken = default) =>
        tool.SearchTours(
            region: request.Region,
            adults: request.Adults,
            start_date: request.StartDate,
            nights: request.Nights,
            children: request.Children,
            hotel_stars: request.HotelStars,
            price_min: request.PriceMin,
            price_max: request.PriceMax,
            currency: request.Currency,
            cancellationToken: cancellationToken);

    /// <summary>Reads the structured content of a result, failing the test when there is none.</summary>
    /// <param name="result">The tool result.</param>
    /// <returns>The payload.</returns>
    internal static JsonElement Structured(CallToolResult result)
    {
        Assert.IsTrue(
            result.StructuredContent.HasValue,
            "The result must carry structured content: an agent reacts to the machine-readable " +
            "payload, not to prose (docs/mcp-tools.md#errors--two-different-layers).");

        return result.StructuredContent!.Value;
    }

    /// <summary>Builds a tool over the given doubles and settings.</summary>
    /// <param name="jobs">The job-queue double.</param>
    /// <param name="regions">The catalog double, defaulting to a catalog holding <c>kemer</c> and <c>alanya</c>.</param>
    /// <param name="options">The settings, defaulting to the documented defaults.</param>
    /// <returns>The tool under test.</returns>
    internal static SearchTools Tool(
        RecordingJobRepository jobs,
        IRegionRepository? regions = null,
        SearchOptions? options = null) =>
        new(jobs, regions ?? new FakeRegionCatalog("kemer", "alanya"), options ?? DefaultOptions());
}

/// <summary>
/// Stands in for the database-backed job queue: it records what the tool asked for and returns the
/// configured answers.
/// </summary>
/// <remarks>
/// What the real repository filters it cannot fake — "only <c>Completed</c>", the reuse window and
/// the "newest first" order are SQL, and they are asserted against the real repository's statement
/// in <c>SearchDedupeTests</c>. What this double proves is the tool's side of the contract: the hash
/// it looks up, the window it passes, the order it calls reuse and the limit in, and the row it
/// writes.
/// </remarks>
internal sealed class RecordingJobRepository : IJobRepository
{
    private readonly SearchJob? _reusableCompletedJob;

    /// <summary>Creates the double, optionally with one reusable job to answer with.</summary>
    /// <param name="reusableCompletedJob">
    /// The job <see cref="FindReusableCompletedAsync"/> returns; <see langword="null"/> means "no
    /// eligible job", which is what the real repository answers for a <c>Pending</c>, <c>Failed</c>
    /// or expired <c>Completed</c> job too.
    /// </param>
    internal RecordingJobRepository(SearchJob? reusableCompletedJob = null)
    {
        _reusableCompletedJob = reusableCompletedJob;
    }

    /// <summary>The active-job count the limit check reads.</summary>
    internal int ActiveJobCount { get; set; }

    /// <summary>Every job the tool asked to persist, in order.</summary>
    internal List<SearchJob> Created { get; } = [];

    /// <summary>How many times the limit check ran — 0 when the call never got that far.</summary>
    internal int CountActiveCalls { get; private set; }

    /// <summary>The hash the reuse lookup used; <see langword="null"/> when it never ran.</summary>
    internal string? LastParametersHash { get; private set; }

    /// <summary>The inclusive lower bound of the window the reuse lookup used.</summary>
    internal DateTime? LastCompletedNotBeforeUtc { get; private set; }

    /// <summary>The token of the last call, whichever method it was.</summary>
    internal CancellationToken LastCancellationToken { get; private set; }

    /// <inheritdoc />
    public Task<SearchJob?> FindReusableCompletedAsync(
        string parametersHash,
        DateTime completedNotBeforeUtc,
        CancellationToken cancellationToken)
    {
        LastParametersHash = parametersHash;
        LastCompletedNotBeforeUtc = completedNotBeforeUtc;
        LastCancellationToken = cancellationToken;

        return Task.FromResult(_reusableCompletedJob);
    }

    /// <inheritdoc />
    public Task<int> CountActiveAsync(CancellationToken cancellationToken)
    {
        CountActiveCalls++;
        LastCancellationToken = cancellationToken;

        return Task.FromResult(ActiveJobCount);
    }

    /// <inheritdoc />
    public Task CreateAsync(SearchJob job, CancellationToken cancellationToken)
    {
        LastCancellationToken = cancellationToken;
        Created.Add(job);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Stands in for the region catalog: a fixed set of active region ids.
/// </summary>
/// <remarks>
/// <see cref="ListActiveAsync"/> throws on purpose. <c>search_tours</c> validates one region id and
/// must not pull the whole catalog to do it; a test that lets that happen would hide the N+1 read.
/// </remarks>
internal sealed class FakeRegionCatalog : IRegionRepository
{
    private readonly HashSet<string> _activeRegionIds;

    /// <summary>Creates the catalog with the regions it sells.</summary>
    /// <param name="activeRegionIds">The active region ids; anything else does not exist.</param>
    internal FakeRegionCatalog(params string[] activeRegionIds)
    {
        _activeRegionIds = [.. activeRegionIds];
    }

    /// <summary>The region id the last existence check asked about.</summary>
    internal string? LastCheckedRegionId { get; private set; }

    /// <summary>The token of the last existence check.</summary>
    internal CancellationToken LastCancellationToken { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<RegionOffersCount>> ListActiveAsync(
        string? query,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "search_tours validates a single region id; it must not list the whole catalog.");

    /// <inheritdoc />
    public Task<bool> ExistsActiveAsync(string regionId, CancellationToken cancellationToken)
    {
        LastCheckedRegionId = regionId;
        LastCancellationToken = cancellationToken;

        return Task.FromResult(_activeRegionIds.Contains(regionId));
    }
}
