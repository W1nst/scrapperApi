using TourSearch.Api.Tools;
using TourSearch.Infrastructure.Regions;

namespace TourSearch.UnitTests.Regions;

/// <summary>
/// Guards the contract of <c>list_regions</c> (docs/mcp-tools.md#list_regions, FR-005): the exact
/// response shape, the computed <c>offers_count</c>, the optional filter and the cancellation token.
/// </summary>
/// <remarks>
/// The catalog is faked, so no database and no network are involved (docs/conventions.md#testing).
/// The SQL the real reader emits — the single grouped count, the <c>active</c> predicate and the
/// case-insensitive filter — is asserted by <c>RegionRepositoryQueryTests</c>, and the behaviour
/// against a real server is verified by the task's own checks.
/// </remarks>
[TestClass]
public sealed class RegionToolsTests
{
    [TestMethod]
    public async Task ListRegions_WhenTheCatalogIsNotEmpty_ReturnsEveryRegionWithItsComputedCount()
    {
        var catalog = new RecordingRegionRepository(
            new RegionOffersCount("kemer", "Кемер", 0),
            new RegionOffersCount("alanya", "Аланья", 7));

        var response = await new RegionTools(catalog).ListRegions(query: null, CancellationToken.None);

        Assert.IsFalse(
            response.Truncated,
            "The catalog is not capped, so it is never truncated; the field stays for a future cap.");
        Assert.HasCount(2, response.Regions);
        Assert.AreEqual("kemer", response.Regions[0].Id);
        Assert.AreEqual("Кемер", response.Regions[0].Label);
        Assert.AreEqual(
            0,
            response.Regions[0].OffersCount,
            "A region with no offers reports 0, not a missing field.");
        Assert.AreEqual(7, response.Regions[1].OffersCount);
    }

    [TestMethod]
    public async Task ListRegions_WhenNothingMatches_ReturnsAnEmptyListAndNotAnError()
    {
        var catalog = new RecordingRegionRepository();

        var response = await new RegionTools(catalog).ListRegions(query: "atlantis", CancellationToken.None);

        Assert.IsEmpty(response.Regions, "No match is a valid empty result, never an error.");
        Assert.IsFalse(response.Truncated);
        Assert.AreEqual("atlantis", catalog.LastQuery, "The filter still reaches the catalog read.");
    }

    [TestMethod]
    public async Task ListRegions_WhenAQueryIsGiven_PassesItTrimmedToTheCatalog()
    {
        var catalog = new RecordingRegionRepository();

        await new RegionTools(catalog).ListRegions(query: "  КЕМЕР  ", CancellationToken.None);

        Assert.AreEqual(
            "КЕМЕР",
            catalog.LastQuery,
            "The filter is trimmed and passed through unchanged: matching is the query's job, and " +
            "case-insensitivity over id and label is what the catalog read implements.");
    }

    [TestMethod]
    [DataRow(null, DisplayName = "absent")]
    [DataRow("", DisplayName = "empty")]
    [DataRow("   ", DisplayName = "whitespace")]
    public async Task ListRegions_WhenTheQueryIsAbsentOrBlank_AsksForTheWholeCatalog(string? query)
    {
        var catalog = new RecordingRegionRepository();

        await new RegionTools(catalog).ListRegions(query, CancellationToken.None);

        Assert.IsNull(
            catalog.LastQuery,
            "A whitespace query means 'all regions', not a filter that matches nothing (FR-005).");
    }

    [TestMethod]
    public async Task ListRegions_WhenCalled_ForwardsTheCallersCancellationToken()
    {
        var catalog = new RecordingRegionRepository();
        using var cancellation = new CancellationTokenSource();

        await new RegionTools(catalog).ListRegions(query: null, cancellation.Token);

        Assert.AreEqual(
            cancellation.Token,
            catalog.LastCancellationToken,
            "The token must reach the query: cancellation means stop and release, never finish the call anyway.");
    }

    /// <summary>
    /// Stands in for the database-backed catalog. It returns its rows regardless of the filter: the
    /// filtering itself happens in SQL, so what this records is that the tool asked for the right
    /// thing and forwarded the token.
    /// </summary>
    private sealed class RecordingRegionRepository(params RegionOffersCount[] rows) : IRegionRepository
    {
        /// <summary>The filter the tool asked for; <see langword="null"/> means the whole catalog.</summary>
        internal string? LastQuery { get; private set; }

        /// <summary>The token the tool passed on.</summary>
        internal CancellationToken LastCancellationToken { get; private set; }

        /// <inheritdoc />
        public Task<bool> ExistsActiveAsync(string regionId, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        /// <inheritdoc />
        public Task<IReadOnlyList<RegionOffersCount>> ListActiveAsync(
            string? query,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            LastCancellationToken = cancellationToken;

            return Task.FromResult<IReadOnlyList<RegionOffersCount>>(rows);
        }
    }
}
