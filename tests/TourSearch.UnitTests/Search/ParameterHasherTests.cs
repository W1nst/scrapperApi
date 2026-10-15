using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TourSearch.Core.Search;

namespace TourSearch.UnitTests.Search;

/// <summary>
/// Guards the canonical form behind <c>ParametersHash</c> (FR-002, ADR 049): the exact canonical
/// JSON, the hash it yields, its independence from the current culture, and the fact that every one
/// of the nine parameters takes part.
/// </summary>
/// <remarks>
/// The golden strings below are deliberate. The canonical form is invisible in review and silently
/// destructive when it changes: a different form invalidates every stored hash and the next
/// identical request creates a duplicate job instead of reusing one. If these tests fail, the form
/// changed — that is a breaking change with an ADR, not a number to update.
/// </remarks>
[TestClass]
public sealed class ParameterHasherTests
{
    /// <summary>The canonical form of the nine fully-populated parameters, keys in ordinal order.</summary>
    private const string ExpectedCanonicalJson =
        """{"adults":2,"children":1,"currency":"RUB","hotel_stars":5,"nights":7,"price_max":1500.50,"price_min":800.5,"region":"kemer","start_date":"2026-06-15"}""";

    private const string ExpectedHash =
        "sha256:6d840cbf28baccdbccc03e8c5e701c0da968535a21d955225654704419dd3437";

    private static readonly SearchRequest FullyPopulated = new(
        Region: "kemer",
        Adults: 2,
        Children: 1,
        HotelStars: 5,
        StartDate: new DateOnly(2026, 6, 15),
        Nights: 7,
        PriceMin: 800.5m,
        PriceMax: 1500.50m,
        Currency: "RUB");

    [TestMethod]
    public void CanonicalJson_WhenEveryParameterIsGiven_IsOneCompactLineWithSortedKeys()
    {
        var canonical = ParameterHasher.CanonicalJson(FullyPopulated);

        Assert.AreEqual(
            ExpectedCanonicalJson,
            canonical,
            "The canonical form is part of the contract: sorted keys, invariant numbers, an " +
            "ISO-8601 day-only date, an explicit currency and JSON null for an absent price bound. " +
            "Changing it invalidates every stored hash (ADR 049).");

        // Sorted keys are what makes the caller's JSON key order irrelevant: the same intent reached
        // the tool through a different property order and must still canonicalise identically.
        var keys = JsonDocument
            .Parse(canonical)
            .RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .ToList();

        CollectionAssert.AreEqual(
            keys.OrderBy(name => name, StringComparer.Ordinal).ToList(),
            keys,
            $"The canonical keys must be in ordinal order, found: {string.Join(", ", keys)}");

        CollectionAssert.AreEquivalent(
            new[]
            {
                "adults", "children", "currency", "hotel_stars", "nights", "price_max", "price_min",
                "region", "start_date",
            },
            keys,
            "The canonical JSON must carry exactly the nine parameters — no more, none missing.");
    }

    [TestMethod]
    public void ComputeHash_WhenTheCanonicalFormIsKnown_IsTheDocumentedSha256Form()
    {
        var hash = ParameterHasher.ComputeHash(FullyPopulated);

        Assert.AreEqual(ExpectedHash, hash, "The hash is the contract: dedupe depends on it (ADR 012).");
        Assert.AreEqual(ParameterHasher.HashLength, hash.Length, "The column is 71 characters wide.");
        Assert.AreEqual(71, hash.Length, "sha256: plus 64 hex characters.");
        Assert.StartsWith("sha256:", hash, StringComparison.Ordinal);
        Assert.AreEqual(
            hash,
            hash.ToLowerInvariant(),
            "The hex digits are lowercase, matching the documented prefix rule.");
        Assert.IsTrue(
            Regex.IsMatch(hash, @"^sha256:[0-9a-f]{64}$"),
            $"Only the prefix and 64 lowercase hex digits are allowed, found '{hash}'.");
    }

    [TestMethod]
    [DataRow("en-US", DisplayName = "the invariant culture")]
    [DataRow("tr-TR", DisplayName = "a culture whose decimal separator is a comma and whose casing folds I")]
    [DataRow("ru-RU", DisplayName = "a culture whose decimal separator is a comma")]
    [DataRow("de-DE", DisplayName = "a culture whose thousands separator is a dot")]
    [DataRow("ar-SA", DisplayName = "a culture with a non-Gregorian default calendar")]
    public void ComputeHash_WhenTheCurrentCultureChanges_IsUnchanged(string cultureName)
    {
        var expectedJson = ParameterHasher.CanonicalJson(FullyPopulated);
        var expectedHash = ParameterHasher.ComputeHash(FullyPopulated);

        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            var culture = new CultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            Assert.AreEqual(
                expectedJson,
                ParameterHasher.CanonicalJson(FullyPopulated),
                $"Culture '{cultureName}' changed the canonical form: a locale-dependent number or " +
                "date format is exactly how the same request would produce two hashes.");

            Assert.AreEqual(
                expectedHash,
                ParameterHasher.ComputeHash(FullyPopulated),
                $"Culture '{cultureName}' changed the hash, so dedupe would silently stop working.");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [TestMethod]
    public void ComputeHash_WhenAnySingleParameterChanges_ChangesTheHash()
    {
        var baseline = SearchFixtures.ValidRequest();
        var baselineHash = ParameterHasher.ComputeHash(baseline);

        var mutations = new (string Parameter, SearchRequest Request)[]
        {
            ("region", baseline with { Region = "alanya" }),
            ("adults", baseline with { Adults = 3 }),
            ("children", baseline with { Children = 2 }),
            ("hotel_stars", baseline with { HotelStars = 5 }),
            ("start_date", baseline with { StartDate = baseline.StartDate.AddDays(1) }),
            ("nights", baseline with { Nights = 8 }),
            ("price_min", baseline with { PriceMin = 501m }),
            ("price_max", baseline with { PriceMax = 1501m }),
            ("currency", baseline with { Currency = "USD" }),
        };

        Assert.HasCount(
            9,
            mutations,
            "All nine parameters must be covered: a parameter missing from the canonical form is a " +
            "parameter two different requests can disagree on while sharing a job.");

        foreach (var (parameter, request) in mutations)
        {
            Assert.AreNotEqual(
                baselineHash,
                ParameterHasher.ComputeHash(request),
                $"Changing '{parameter}' must change the hash, or two different searches would share one job.");
        }

        Assert.AreEqual(
            mutations.Length,
            mutations.Select(mutation => ParameterHasher.ComputeHash(mutation.Request)).Distinct().Count(),
            "Every mutated request must hash differently from every other one too.");
    }

    [TestMethod]
    public void CanonicalJson_WhenAPriceBoundIsAbsent_CarriesNullAndDiffersFromZero()
    {
        var baseline = SearchFixtures.ValidRequest();

        var absent = ParameterHasher.CanonicalJson(baseline with { PriceMin = null, PriceMax = null });
        var zero = ParameterHasher.CanonicalJson(baseline with { PriceMin = 0m, PriceMax = 0m });

        StringAssert.Contains(
            absent,
            "\"price_min\":null",
            "An absent bound is JSON null, not a sentinel: 'no lower bound' must not be stored as a bound.");
        StringAssert.Contains(absent, "\"price_max\":null");
        StringAssert.Contains(zero, "\"price_min\":0");

        Assert.AreNotEqual(
            ParameterHasher.ComputeHash(baseline with { PriceMin = null, PriceMax = null }),
            ParameterHasher.ComputeHash(baseline with { PriceMin = 0m, PriceMax = 0m }),
            "'price_min: 0' is a bound the caller asked for; absent is not. The hash must tell them apart.");
    }

    [TestMethod]
    public void CanonicalJson_WhenADateIsGiven_CarriesTheDayOnly()
    {
        var canonical = ParameterHasher.CanonicalJson(
            SearchFixtures.ValidRequest() with { StartDate = new DateOnly(2026, 6, 15) });

        StringAssert.Contains(
            canonical,
            "\"start_date\":\"2026-06-15\"",
            "The date contributes a day, not an instant: the same check-in date must hash identically " +
            "whatever time of day the request was made.");
        Assert.IsFalse(
            canonical.Contains("T00:00", StringComparison.Ordinal),
            $"No time component may appear in the canonical form: {canonical}");
    }
}
