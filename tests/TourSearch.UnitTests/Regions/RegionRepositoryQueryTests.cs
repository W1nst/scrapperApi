using Microsoft.EntityFrameworkCore;
using TourSearch.Infrastructure;
using TourSearch.Infrastructure.Regions;

namespace TourSearch.UnitTests.Regions;

/// <summary>
/// Guards the shape of the catalog read behind <c>list_regions</c> (NFR-002, FR-005): one statement,
/// one grouped offer count, only active regions, a case-insensitive substring filter that treats
/// <c>%</c> literally, and a deterministic order.
/// </summary>
/// <remarks>
/// The statement is built, never executed: <see cref="RegionRepository.BuildListStatement"/> opens no
/// connection, exactly like the model-script assertions in <c>ModelShapeTests</c>. That is what keeps
/// this a unit test — no test in this project contacts a database (docs/conventions.md#testing).
/// </remarks>
[TestClass]
public sealed class RegionRepositoryQueryTests
{
    private const string PlaceholderConnectionString = "Host=localhost;Database=toursearch";

    [TestMethod]
    public void BuildListStatement_WhenTheWholeCatalogIsRequested_IsOneStatementWithOneGroupedCount()
    {
        var sql = Statement(null);

        StringAssert.Contains(sql, "tour_search.regions", "The catalog is read from the regions table.");
        StringAssert.Contains(sql, "tour_search.tour_offers", "The counts come from the offers table.");
        StringAssert.Contains(sql, "LEFT JOIN", "A region with no offers must still produce a row.");
        StringAssert.Contains(sql, "r.active", "Only regions the source currently sells are returned (FR-005).");

        // NFR-002: the offer count is one grouped query, never one query per region.
        Assert.AreEqual(
            1,
            Matches(sql, "GROUP BY"),
            $"The offer count must be grouped once, not per region (NFR-002). Statement:\n{sql}");
        Assert.AreEqual(
            1,
            Matches(sql, "tour_search.tour_offers"),
            $"One grouped read of tour_offers, not an N+1 loop (NFR-002). Statement:\n{sql}");
        Assert.AreEqual(
            0,
            Matches(sql, ";"),
            $"A single statement has no statement separator. Statement:\n{sql}");

        // Deterministic order so repeated calls are stable (FR-005): label first, id as the tiebreak.
        StringAssert.Contains(
            sql,
            "ORDER BY r.label, r.id",
            $"Ordering must be (label, id). Statement:\n{sql}");
    }

    [TestMethod]
    public void BuildListStatement_WhenAQueryIsGiven_FiltersCaseInsensitivelyOnIdAndLabel()
    {
        var sql = Statement("КЕМЕР");

        // A substring match, and case-insensitive because both sides are lowered — the id and the
        // label (FR-005).
        StringAssert.Contains(sql, "lower(r.id) LIKE", $"The filter covers the region id. Statement:\n{sql}");
        StringAssert.Contains(sql, "lower(r.label) LIKE", $"The filter covers the region label. Statement:\n{sql}");

        // Still one statement with one grouped count: filtering must not multiply the read.
        Assert.AreEqual(1, Matches(sql, "GROUP BY"), $"Filtering must not change the read shape. Statement:\n{sql}");
    }

    [TestMethod]
    public void BuildListStatement_WhenTheQueryContainsWildcardCharacters_EscapesThemIntoLiterals()
    {
        var sql = Statement("50%_x");

        // '%' and '_' are literal text, not SQL wildcards: the translated pattern escapes them, so a
        // query for "50%" cannot match every region (task edge cases). PostgreSQL reads a backslash in
        // a LIKE pattern as the escape character by default, so "\%" is a literal percent sign.
        StringAssert.Contains(sql, @"\%", $"A percent sign in the query must be escaped. Statement:\n{sql}");
        StringAssert.Contains(sql, @"\_", $"An underscore in the query must be escaped. Statement:\n{sql}");
    }

    [TestMethod]
    public void BuildListStatement_WhenTheQueryIsBlank_AsksForTheWholeCatalog()
    {
        Assert.AreEqual(
            Statement(null),
            Statement("   "),
            "A whitespace query means 'all regions': it must not become a filter (FR-005).");
    }

    private static string Statement(string? query)
    {
        using var context = new TourSearchDbContext(
            new DbContextOptionsBuilder<TourSearchDbContext>()
                .UseNpgsql(PlaceholderConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options);

        return new RegionRepository(context).BuildListStatement(query).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static int Matches(string sql, string token) =>
        sql.Split(token, StringSplitOptions.None).Length - 1;
}
