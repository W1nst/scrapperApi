using System.Reflection;
using TourSearch.Core.Entities;
using TourSearch.Core.Enums;

namespace TourSearch.UnitTests.Entities;

/// <summary>
/// Guards the shape of the <see cref="TourSearch.Core"/> model against the field tables in
/// docs/domain.md: property count, CLR type and nullability (FR-001), plus the enum member
/// sets and their declaration order (FR-002).
/// </summary>
[TestClass]
public sealed class EntityShapeTests
{
    // NOT static: NullabilityInfoContext caches internally and is not safe for concurrent use.
    // A shared instance makes these tests flaky under the assembly's method-level
    // parallelization (ADR 036): concurrent Create() calls race on the cache and throw
    // "An item with the same key has already been added. Key: <type>" (System.ArgumentException).
    // Reproduced by stressing a shared instance from 32 threads: 4-5 failures per 300 iterations.
    // Constructing it per call is cheap, so each test gets its own.

    [TestMethod]
    public void SearchJob_Properties_MatchTheDocumentedTwentyOneFields()
    {
        AssertPropertiesMatch(typeof(SearchJob), new Dictionary<string, Type>
        {
            ["Id"] = typeof(Guid),
            ["ParametersHash"] = typeof(string),
            ["Parameters"] = typeof(SearchParameters),
            ["CurrencyCode"] = typeof(string),
            ["Status"] = typeof(JobStatus),
            ["Attempt"] = typeof(int),
            ["LeaseToken"] = typeof(string),
            ["LeaseExpiresAt"] = typeof(DateTime?),
            ["CancelRequestedAt"] = typeof(DateTime?),
            ["CreatedAt"] = typeof(DateTime),
            ["StartedAt"] = typeof(DateTime?),
            ["CompletedAt"] = typeof(DateTime?),
            ["Degraded"] = typeof(bool),
            ["Warnings"] = typeof(List<string>),
            ["OffersCount"] = typeof(int),
            ["PagesDone"] = typeof(int),
            ["PagesTotal"] = typeof(int?),
            ["ErrorCode"] = typeof(JobErrorCode?),
            ["ErrorMessage"] = typeof(string),
            ["Retryable"] = typeof(bool?),
            ["Offers"] = typeof(List<TourOffer>),
        });

        AssertNullableReferences(typeof(SearchJob), new Dictionary<string, bool>
        {
            ["ParametersHash"] = false,
            ["Parameters"] = false,
            ["CurrencyCode"] = false,
            ["LeaseToken"] = true,
            ["Warnings"] = false,
            ["ErrorMessage"] = true,
            ["Offers"] = false,
        });
    }

    [TestMethod]
    public void SearchParameters_Properties_MatchTheDocumentedEightFields()
    {
        AssertPropertiesMatch(typeof(SearchParameters), new Dictionary<string, Type>
        {
            ["Region"] = typeof(string),
            ["Adults"] = typeof(int),
            ["Children"] = typeof(int),
            ["HotelStars"] = typeof(int),
            ["StartDate"] = typeof(DateTime),
            ["Nights"] = typeof(int),
            ["PriceMin"] = typeof(decimal),
            ["PriceMax"] = typeof(decimal),
        });

        AssertNullableReferences(typeof(SearchParameters), new Dictionary<string, bool>
        {
            ["Region"] = false,
        });
    }

    [TestMethod]
    public void TourOffer_Properties_MatchTheDocumentedFourteenFieldsWithoutRaw()
    {
        AssertPropertiesMatch(typeof(TourOffer), new Dictionary<string, Type>
        {
            ["Id"] = typeof(Guid),
            ["SearchJobId"] = typeof(Guid),
            ["HotelName"] = typeof(string),
            ["HotelStars"] = typeof(int),
            ["Luxury"] = typeof(bool),
            ["RegionId"] = typeof(string),
            ["CheckIn"] = typeof(DateTime),
            ["Nights"] = typeof(int),
            ["Price"] = typeof(decimal),
            ["CurrencyCode"] = typeof(string),
            ["RoomType"] = typeof(string),
            ["MealType"] = typeof(string),
            ["SourceUrl"] = typeof(string),
            ["ScrapedAt"] = typeof(DateTime),
        });

        AssertNullableReferences(typeof(TourOffer), new Dictionary<string, bool>
        {
            ["HotelName"] = false,
            ["RegionId"] = false,
            ["CurrencyCode"] = false,
            ["RoomType"] = true,
            ["MealType"] = true,
            ["SourceUrl"] = true,
        });
    }

    [TestMethod]
    public void Region_Properties_MatchTheDocumentedFourFields()
    {
        AssertPropertiesMatch(typeof(Region), new Dictionary<string, Type>
        {
            ["Id"] = typeof(string),
            ["Label"] = typeof(string),
            ["Source"] = typeof(string),
            ["Active"] = typeof(bool),
        });

        AssertNullableReferences(typeof(Region), new Dictionary<string, bool>
        {
            ["Id"] = false,
            ["Label"] = false,
            ["Source"] = false,
        });
    }

    [TestMethod]
    public void JobStatus_Members_AreDeclaredInTheDocumentedOrder()
    {
        CollectionAssert.AreEqual(
            new[] { "Pending", "InProgress", "Completed", "Failed", "Cancelled" },
            Enum.GetNames<JobStatus>());
    }

    [TestMethod]
    public void JobErrorCode_Members_AreExactlyTheTwelveTaxonomyCodes()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                "NETWORK_TIMEOUT", "NETWORK_UNREACHABLE", "HTTP_429", "HTTP_5XX", "BROWSER_CRASH",
                "LEASE_EXPIRED", "SELECTOR_NOT_FOUND", "PAGE_STRUCTURE_CHANGED", "ANTI_BOT_DETECTED",
                "PARAM_INVALID", "JOB_NOT_FOUND", "INTERNAL",
            },
            Enum.GetNames<JobErrorCode>());
    }

    private static void AssertPropertiesMatch(Type entityType, IReadOnlyDictionary<string, Type> expected)
    {
        var actual = entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, p => p.PropertyType);

        CollectionAssert.AreEquivalent(
            expected.Keys.ToList(),
            actual.Keys.ToList(),
            $"{entityType.Name} does not expose the documented property set.");

        foreach (var (name, type) in expected)
        {
            Assert.AreEqual(type, actual[name], $"{entityType.Name}.{name} has the wrong type.");
        }
    }

    private static void AssertNullableReferences(Type entityType, IReadOnlyDictionary<string, bool> expected)
    {
        foreach (var (name, nullable) in expected)
        {
            var property = entityType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(property, $"{entityType.Name}.{name} is missing.");

            var state = new NullabilityInfoContext().Create(property).WriteState;
            var actual = state == NullabilityState.Nullable;
            Assert.AreEqual(
                nullable,
                actual,
                $"{entityType.Name}.{name} nullability does not match docs/domain.md (was {state}).");
        }
    }
}
