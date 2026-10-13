using System.Reflection;
using TourSearch.Core.Entities;
using TourSearch.Core.Enums;

namespace TourSearch.UnitTests.Entities;

/// <summary>
/// Guards the rule that every persisted timestamp is UTC (docs/database.md#utc-trap,
/// FR-007): Npgsql throws on a non-UTC <see cref="DateTime"/> instead of converting it, so
/// a <c>Local</c> or <c>Unspecified</c> value is a runtime failure waiting to happen.
/// </summary>
/// <remarks>
/// The check is made over the CLR model, not the database: it needs no connection and no
/// migration. "Supported construction path" here means the entity's own settable
/// properties, populated with <see cref="DateTimeKind.Utc"/> values — there is no factory
/// and no ORM trickery between the caller and the field.
/// </remarks>
[TestClass]
public sealed class UtcKindTests
{
    private static readonly DateTime UtcSample = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void PersistedTimestamps_WhenPopulatedThroughTheEntityApi_AreAllUtcKind()
    {
        foreach (var entity in SupportedEntities())
        {
            var uncovered = UncoveredTimestamps(entity).ToList();
            Assert.IsEmpty(
                uncovered,
                $"{entity.GetType().Name} has timestamp properties this fixture does not set: " +
                $"{string.Join(", ", uncovered)}. Every persisted DateTime must be covered here.");

            var violations = NonUtcTimestamps(entity).ToList();
            Assert.IsEmpty(
                violations,
                $"Every persisted timestamp must be DateTimeKind.Utc (docs/database.md#utc-trap), " +
                $"but the supported construction path produced: {string.Join("; ", violations)}");
        }
    }

    [TestMethod]
    public void DefaultConstruction_DoesNotPrePopulateANonUtcTimestamp()
    {
        // A property initialiser like `= DateTime.Now` would slip past the fixture above,
        // because the fixture overwrites every property it knows about.
        var freshlyConstructed = new object[]
        {
            new SearchJob(),
            new SearchParameters(),
            new TourOffer(),
            new Region(),
        };

        var violations = freshlyConstructed.SelectMany(NonUtcTimestamps).ToList();
        Assert.IsEmpty(
            violations,
            $"A freshly constructed entity must not carry a non-UTC timestamp: {string.Join("; ", violations)}");
    }

    [TestMethod]
    public void NonUtcTimestamp_IsReportedWithTheOffendingPropertyName()
    {
        var job = CreateSearchJob();
        job.CreatedAt = DateTime.SpecifyKind(UtcSample, DateTimeKind.Local);
        job.LeaseExpiresAt = DateTime.SpecifyKind(UtcSample, DateTimeKind.Unspecified);

        var violations = NonUtcTimestamps(job).ToList();

        Assert.HasCount(2, violations, $"Unexpected violations: {string.Join("; ", violations)}");

        var local = violations.Single(v => v.StartsWith($"{nameof(SearchJob)}.{nameof(SearchJob.CreatedAt)} ", StringComparison.Ordinal));
        var unspecified = violations.Single(v => v.StartsWith($"{nameof(SearchJob)}.{nameof(SearchJob.LeaseExpiresAt)} ", StringComparison.Ordinal));

        StringAssert.Contains(local, nameof(DateTimeKind.Local));
        StringAssert.Contains(unspecified, nameof(DateTimeKind.Unspecified));
    }

    private static IEnumerable<object> SupportedEntities() =>
        new object[]
        {
            CreateSearchJob(),
            CreateSearchParameters(),
            CreateTourOffer(),
            CreateRegion(),
        };

    private static SearchJob CreateSearchJob() => new()
    {
        Id = Guid.NewGuid(),
        ParametersHash = "sha256:0000000000000000000000000000000000000000000000000000000000000000",
        Parameters = CreateSearchParameters(),
        Status = JobStatus.Pending,
        LeaseExpiresAt = UtcSample,
        CancelRequestedAt = UtcSample,
        CreatedAt = UtcSample,
        StartedAt = UtcSample,
        CompletedAt = UtcSample,
    };

    private static SearchParameters CreateSearchParameters() => new()
    {
        Region = "antalya",
        StartDate = UtcSample,
    };

    private static TourOffer CreateTourOffer() => new()
    {
        Id = Guid.NewGuid(),
        SearchJobId = Guid.NewGuid(),
        HotelName = "Sample Hotel",
        RegionId = "antalya",
        CheckIn = UtcSample,
        ScrapedAt = UtcSample,
    };

    private static Region CreateRegion() => new()
    {
        Id = "antalya",
        Label = "Antalya",
        Source = "tourvisor",
    };

    /// <summary>
    /// Names every <see cref="DateTime"/>/<see cref="DateTime?"/> property that holds a value
    /// whose <see cref="DateTime.Kind"/> is not <see cref="DateTimeKind.Utc"/>. A null value
    /// and <c>default</c> are not values, so they are skipped here and caught by
    /// <see cref="UncoveredTimestamps"/> instead.
    /// </summary>
    private static IEnumerable<string> NonUtcTimestamps(object entity)
    {
        foreach (var property in TimestampProperties(entity.GetType()))
        {
            var value = (DateTime?)property.GetValue(entity);
            if (value is null || value.Value == default)
            {
                continue;
            }

            if (value.Value.Kind != DateTimeKind.Utc)
            {
                yield return $"{entity.GetType().Name}.{property.Name} is {value.Value.Kind}, not Utc";
            }
        }
    }

    /// <summary>Names every timestamp property the fixture left at <c>null</c>/<c>default</c>.</summary>
    private static IEnumerable<string> UncoveredTimestamps(object entity)
    {
        foreach (var property in TimestampProperties(entity.GetType()))
        {
            var value = (DateTime?)property.GetValue(entity);
            if (value is null || value.Value == default)
            {
                yield return property.Name;
            }
        }
    }

    /// <summary>
    /// A timestamp property is either <see cref="DateTime"/> (boxed as a non-null
    /// <see cref="DateTime"/>) or <see cref="DateTime?"/>; both read back through a cast to
    /// <see cref="Nullable{T}"/>, so the two cases are handled by one loop.
    /// </summary>
    private static IEnumerable<PropertyInfo> TimestampProperties(Type entityType) =>
        entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(DateTime) || p.PropertyType == typeof(DateTime?));
}
