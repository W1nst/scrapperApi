using System.Globalization;
using TourSearch.Core.Search;

namespace TourSearch.UnitTests.Search;

/// <summary>
/// Guards FR-001: every parameter is validated <b>before</b> anything is stored, a violation is a
/// <b>tool-level</b> error carrying <c>PARAM_INVALID</c> and the offending field, and a rejected
/// request leaves no row behind (ADR 038, NFR-001).
/// </summary>
/// <remarks>
/// The tool is the only layer that validates, so these tests drive the tool the way a client does —
/// with the contract's own argument values — and read the payload an agent would read.
/// </remarks>
[TestClass]
public sealed class SearchValidationTests
{
    [TestMethod]
    [DataRow("region not in the catalog", "region", "atlantis", DisplayName = "an invented region id")]
    [DataRow("region is a display label", "region", "Кемер", DisplayName = "a region label instead of its id")]
    [DataRow("region is blank", "region", "", DisplayName = "an empty region")]
    [DataRow("adults", "adults", "0", DisplayName = "adults = 0")]
    [DataRow("children", "children", "-1", DisplayName = "children = -1")]
    [DataRow("hotel_stars", "hotel_stars", "6", DisplayName = "hotel_stars = 6")]
    [DataRow("start_date", "start_date", "-1", DisplayName = "a check-in date in the past")]
    [DataRow("nights", "nights", "0", DisplayName = "nights = 0")]
    [DataRow("price_min", "price_min", "-1", DisplayName = "a negative price_min")]
    [DataRow("price_max", "price_max", "400", DisplayName = "price_max below price_min")]
    [DataRow("price_max", "price_max", "-1", DisplayName = "a negative price_max next to a positive price_min")]
    [DataRow("currency", "currency", "USD", DisplayName = "currency = USD")]
    [DataRow("currency lower-case", "currency", "rub", DisplayName = "currency = rub")]
    public async Task SearchTours_WhenOneParameterBreaksItsRule_RejectsWithParamInvalidNamingThatField(
        string caseName,
        string field,
        string value)
    {
        var jobs = new RecordingJobRepository();
        var regions = new FakeRegionCatalog("kemer", "alanya");
        var tool = SearchFixtures.Tool(jobs, regions);
        var request = WithInvalidValue(field, value);

        var result = await SearchFixtures.CallAsync(tool, request);

        Assert.IsTrue(
            result.IsError ?? false,
            $"'{caseName}' must come back as a tool-level error (isError: true): the transport " +
            "worked and the request itself is the problem (FR-001).");

        var payload = SearchFixtures.Structured(result);

        Assert.AreEqual(
            "PARAM_INVALID",
            payload.GetProperty("error_code").GetString(),
            $"'{caseName}' must be reported as PARAM_INVALID, and it is never persisted: no job " +
            "exists to fail (docs/domain.md#error-taxonomy).");

        Assert.AreEqual(
            field,
            payload.GetProperty("field").GetString(),
            $"'{caseName}' must name the field the caller has to fix.");

        var message = payload.GetProperty("message").GetString();
        Assert.IsNotNull(message, "The message is what a human reads; it must be present.");
        StringAssert.Contains(
            message,
            "PARAM_INVALID",
            "The text block must carry the code too: a client that reads only text must still see it.");
        StringAssert.Contains(
            message,
            field,
            $"The message of '{caseName}' must name the offending field, not only the rule.");

        Assert.IsEmpty(
            jobs.Created,
            $"'{caseName}' must be rejected BEFORE anything is persisted: a rejected request leaves " +
            "no row (FR-001).");
        Assert.IsNull(
            jobs.LastParametersHash,
            $"'{caseName}' must be rejected before the reuse lookup even runs.");
        Assert.AreEqual(
            0,
            jobs.CountActiveCalls,
            $"'{caseName}' must be rejected before the limit check runs.");
    }

    [TestMethod]
    public async Task SearchTours_WhenTheCancellationTokenIsAlreadyCancelled_ThrowsInsteadOfWriting()
    {
        var jobs = new RecordingJobRepository();
        var tool = SearchFixtures.Tool(jobs);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => SearchFixtures.CallAsync(tool, SearchFixtures.ValidRequest(), cancellation.Token));

        Assert.IsEmpty(jobs.Created, "A cancelled call must not have written anything.");
    }

    [TestMethod]
    public async Task SearchTours_WhenCalled_ForwardsTheCallersCancellationTokenToEveryLayer()
    {
        var jobs = new RecordingJobRepository();
        var regions = new FakeRegionCatalog("kemer");
        var tool = SearchFixtures.Tool(jobs, regions);
        using var cancellation = new CancellationTokenSource();

        await SearchFixtures.CallAsync(tool, SearchFixtures.ValidRequest(), cancellation.Token);

        Assert.AreEqual(
            cancellation.Token,
            regions.LastCancellationToken,
            "The token must reach the catalog check (docs/conventions.md#cancellation).");
        Assert.AreEqual(
            cancellation.Token,
            jobs.LastCancellationToken,
            "The token must reach the job queue — the lookup, the count and the insert.");
    }

    [TestMethod]
    [DataRow(0, DisplayName = "hotel_stars = 0 means any or unknown and is accepted")]
    [DataRow(1, DisplayName = "hotel_stars = 1")]
    [DataRow(5, DisplayName = "hotel_stars = 5")]
    public async Task SearchTours_WhenHotelStarsIsInRange_CreatesTheJob(int hotelStars)
    {
        var jobs = new RecordingJobRepository();
        var tool = SearchFixtures.Tool(jobs);
        var request = SearchFixtures.ValidRequest() with { HotelStars = hotelStars };

        var result = await SearchFixtures.CallAsync(tool, request);

        Assert.IsFalse(result.IsError ?? false, $"hotel_stars = {hotelStars} is a legal value.");
        Assert.HasCount(1, jobs.Created);
        Assert.AreEqual(hotelStars, jobs.Created[0].Parameters.HotelStars);
    }

    [TestMethod]
    public async Task SearchTours_WhenAPriceBoundIsAbsent_CreatesTheJobWithNoBound()
    {
        var jobs = new RecordingJobRepository();
        var tool = SearchFixtures.Tool(jobs);

        var result = await SearchFixtures.CallAsync(
            tool,
            SearchFixtures.ValidRequest() with { PriceMin = null, PriceMax = null });

        Assert.IsFalse(result.IsError ?? false, "Absent bounds mean unbounded (FR-001 edge cases).");
        Assert.HasCount(1, jobs.Created);
        Assert.IsNull(
            jobs.Created[0].Parameters.PriceMin,
            "An absent lower bound is stored as absent, not as a bound the caller never asked for.");
        Assert.IsNull(jobs.Created[0].Parameters.PriceMax);
    }

    [TestMethod]
    public async Task SearchTours_WhenOnlyPriceMinIsGiven_AcceptsItWithoutAnUpperBound()
    {
        var jobs = new RecordingJobRepository();
        var tool = SearchFixtures.Tool(jobs);

        var result = await SearchFixtures.CallAsync(
            tool,
            SearchFixtures.ValidRequest() with { PriceMin = 800m, PriceMax = null });

        Assert.IsFalse(result.IsError ?? false, "price_max >= price_min is checked only when both are given.");
        Assert.HasCount(1, jobs.Created);
        Assert.AreEqual(800m, jobs.Created[0].Parameters.PriceMin);
        Assert.IsNull(jobs.Created[0].Parameters.PriceMax);
    }

    [TestMethod]
    public async Task SearchTours_WhenTheCurrencyIsAbsent_UsesTheDocumentedRubDefault()
    {
        var jobs = new RecordingJobRepository();
        var tool = SearchFixtures.Tool(jobs);
        var request = SearchFixtures.ValidRequest() with { Currency = "RUB" };

        var result = await tool.SearchTours(
            region: request.Region,
            adults: request.Adults,
            start_date: request.StartDate,
            nights: request.Nights);

        Assert.IsFalse(result.IsError ?? false, "currency is optional and defaults to RUB.");
        Assert.HasCount(1, jobs.Created);
        Assert.AreEqual(
            "RUB",
            jobs.Created[0].CurrencyCode,
            "The default is the same value the caller could have sent, so both hash identically.");
    }

    [TestMethod]
    public async Task SearchTours_WhenChildrenAreAbsent_UsesZero()
    {
        var jobs = new RecordingJobRepository();
        var tool = SearchFixtures.Tool(jobs);
        var request = SearchFixtures.ValidRequest();

        var result = await tool.SearchTours(
            region: request.Region,
            adults: request.Adults,
            start_date: request.StartDate,
            nights: request.Nights);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual(0, jobs.Created[0].Parameters.Children, "An absent party of children is no children.");
    }

    [TestMethod]
    public async Task SearchTours_WhenStartDateIsTodayUtc_IsAccepted()
    {
        var jobs = new RecordingJobRepository();
        var tool = SearchFixtures.Tool(jobs);

        var result = await SearchFixtures.CallAsync(
            tool,
            SearchFixtures.ValidRequest() with { StartDate = SearchFixtures.TodayUtc });

        Assert.IsFalse(
            result.IsError ?? false,
            "The rule is day precision in UTC: today is valid, only a date strictly before it is not.");
    }

    [TestMethod]
    public async Task SearchTours_WhenTheRegionIsInactive_RejectsItLikeAnUnknownOne()
    {
        var jobs = new RecordingJobRepository();

        // The catalog double only sells "kemer": an inactive region and an invented one are answered
        // the same way, which is the point — the agent must go to list_regions, not guess which.
        var tool = SearchFixtures.Tool(jobs, new FakeRegionCatalog("kemer"));

        var result = await SearchFixtures.CallAsync(tool, SearchFixtures.ValidRequest() with { Region = "alanya" });

        Assert.IsTrue(result.IsError ?? false, "A region the source stopped selling must not be searchable.");
        Assert.AreEqual("region", SearchFixtures.Structured(result).GetProperty("field").GetString());
        Assert.IsEmpty(jobs.Created);
    }

    /// <summary>
    /// Mutates exactly one parameter of the baseline request, so a failure names that parameter.
    /// </summary>
    /// <param name="field">The contract's field name to break.</param>
    /// <param name="value">The value to break it with; dates are day offsets from today's UTC date.</param>
    /// <returns>The request with one illegal value.</returns>
    private static SearchRequest WithInvalidValue(string field, string value)
    {
        var baseline = SearchFixtures.ValidRequest();

        return field switch
        {
            "region" => baseline with { Region = value },
            "adults" => baseline with { Adults = ParseInt(value) },
            "children" => baseline with { Children = ParseInt(value) },
            "hotel_stars" => baseline with { HotelStars = ParseInt(value) },
            "start_date" => baseline with { StartDate = SearchFixtures.TodayUtc.AddDays(ParseInt(value)) },
            "nights" => baseline with { Nights = ParseInt(value) },
            "price_min" => baseline with { PriceMin = ParseDecimal(value) },
            "price_max" => baseline with { PriceMax = ParseDecimal(value) },
            "currency" => baseline with { Currency = value },
            _ => throw new InvalidOperationException($"'{field}' is not a search parameter (test bug)."),
        };
    }

    private static int ParseInt(string value) => int.Parse(value, CultureInfo.InvariantCulture);

    private static decimal ParseDecimal(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
