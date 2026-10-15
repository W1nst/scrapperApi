using System.Globalization;
using System.Text.Json;
using TourSearch.Api;
using TourSearch.Core.Entities;
using TourSearch.Core.Enums;
using TourSearch.Core.Search;

namespace TourSearch.UnitTests.Search;

/// <summary>
/// Guards FR-005: the created row carries every documented field with the documented value, the job
/// is created <b>unclaimed</b>, and the response is exactly the five fields of the contract
/// (docs/mcp-tools.md#search_tours).
/// </summary>
/// <remarks>
/// The row is inspected after the tool has written it through the repository double, so this is the
/// job the Worker would later claim — every field it reads, set or null, is asserted here.
/// </remarks>
[TestClass]
public sealed class SearchResponseContractTests
{
    /// <summary>The five fields of the contract, and nothing else.</summary>
    private static readonly string[] ContractFields =
    [
        "job_id", "status", "reused", "parameters_hash", "poll_after_ms",
    ];

    [TestMethod]
    public async Task SearchTours_WhenAJobIsCreated_WritesEveryDocumentedFieldOfTheRow()
    {
        var jobs = new RecordingJobRepository { ActiveJobCount = 0 };
        var tool = SearchFixtures.Tool(jobs);
        var request = SearchFixtures.ValidRequest();
        var before = DateTime.UtcNow;

        await SearchFixtures.CallAsync(tool, request);

        Assert.HasCount(1, jobs.Created);
        var job = jobs.Created[0];

        Assert.AreNotEqual(
            Guid.Empty,
            job.Id,
            "The job gets a fresh client-side Guid: PostgreSQL 17 has no uuidv7() (docs/domain.md#guid-ordering).");

        Assert.AreEqual(
            ParameterHasher.ComputeHash(request),
            job.ParametersHash,
            "The stored hash is the canonical hash of the request and nothing else (FR-002).");

        Assert.AreEqual(
            SearchFixtures.ValidRequest().Region,
            job.Parameters.Region,
            "The slug the caller passed is stored, not a label or a source id.");
        Assert.AreEqual(request.Adults, job.Parameters.Adults);
        Assert.AreEqual(request.Children, job.Parameters.Children);
        Assert.AreEqual(request.HotelStars, job.Parameters.HotelStars);
        Assert.AreEqual(request.Nights, job.Parameters.Nights);
        Assert.AreEqual(request.PriceMin, job.Parameters.PriceMin);
        Assert.AreEqual(request.PriceMax, job.Parameters.PriceMax);
        Assert.AreEqual(
            request.StartDate,
            DateOnly.FromDateTime(job.Parameters.StartDate),
            "The check-in date round-trips at day precision.");
        Assert.AreEqual(
            DateTimeKind.Utc,
            job.Parameters.StartDate.Kind,
            "Every persisted timestamp is UTC or Npgsql throws (docs/database.md#utc-trap).");

        Assert.AreEqual(request.Currency, job.CurrencyCode, "The currency lives on the job, not in the parameters.");

        Assert.AreEqual(
            JobStatus.Pending,
            job.Status,
            "The tool creates work; it does not execute it, and it never writes any other status.");

        Assert.AreEqual(0, job.Attempt, "Attempt counts claims: this job has never been claimed.");
        Assert.AreEqual(
            DateTimeKind.Utc,
            job.CreatedAt.Kind,
            "CreatedAt is UTC (docs/database.md#utc-trap).");
        Assert.IsTrue(
            job.CreatedAt >= before && job.CreatedAt <= DateTime.UtcNow,
            $"CreatedAt must be the moment of creation, found {job.CreatedAt:O}.");
        Assert.IsFalse(job.Degraded, "A fresh job has parsed nothing yet, so it is not degraded.");
        Assert.IsEmpty(job.Warnings, "Nothing has been parsed, so there is nothing to warn about.");
        Assert.AreEqual(0, job.OffersCount, "The running offer counter starts at zero.");
        Assert.AreEqual(0, job.PagesDone, "The running page counter starts at zero.");
        Assert.IsNull(
            job.PagesTotal,
            "The expected page count is unknown until the source is read (docs/domain.md).");

        // The claim fields belong to the Worker's atomic compare-and-set (ADR 005). Writing any of
        // them here would double-claim the job the moment a Worker exists.
        Assert.IsNull(job.LeaseToken, "The job is not claimed: no lease token.");
        Assert.IsNull(job.LeaseExpiresAt, "The job is not claimed: no lease expiry.");
        Assert.IsNull(job.StartedAt, "The job has not started.");
        Assert.IsNull(job.CompletedAt, "The job has not finished.");
        Assert.IsNull(job.CancelRequestedAt, "Nobody asked to cancel it.");

        // No failure is persisted at creation: a transient error must never be born into the row.
        Assert.IsNull(job.ErrorCode, "A created job is not a failed job.");
        Assert.IsNull(job.ErrorMessage);
        Assert.IsNull(job.Retryable);
    }

    [TestMethod]
    public async Task SearchTours_WhenAJobIsCreated_ReturnsExactlyTheContractFields()
    {
        var jobs = new RecordingJobRepository { ActiveJobCount = 0 };
        var tool = SearchFixtures.Tool(jobs, options: new SearchOptions(TimeSpan.FromHours(24), 10, 1234));

        var result = await SearchFixtures.CallAsync(tool, SearchFixtures.ValidRequest());

        Assert.IsFalse(result.IsError ?? false);

        var payload = SearchFixtures.Structured(result);
        CollectionAssert.AreEquivalent(
            ContractFields,
            payload.EnumerateObject().Select(property => property.Name).ToList(),
            "docs/mcp-tools.md#search_tours fixes exactly five output fields.");

        Assert.AreEqual(jobs.Created[0].Id, payload.GetProperty("job_id").GetGuid());
        Assert.AreEqual(
            "Pending",
            payload.GetProperty("status").GetString(),
            "The enum is emitted as its member name, which is the contract's string.");
        Assert.IsFalse(payload.GetProperty("reused").GetBoolean());
        Assert.AreEqual(ParameterHasher.ComputeHash(SearchFixtures.ValidRequest()), payload.GetProperty("parameters_hash").GetString());
        Assert.AreEqual(ParameterHasher.HashLength, payload.GetProperty("parameters_hash").GetString()!.Length);
        Assert.AreEqual(
            1234,
            payload.GetProperty("poll_after_ms").GetInt32(),
            "poll_after_ms is a setting that tracks the real pickup latency, not a literal in the tool.");
    }

    [TestMethod]
    public async Task SearchTours_WhenAJobIsReused_ReturnsTheSameFiveFieldsForTheExistingJob()
    {
        var existingJobId = Guid.NewGuid();
        var jobs = new RecordingJobRepository(new SearchJob
        {
            Id = existingJobId,
            Status = JobStatus.Completed,
            CompletedAt = DateTime.UtcNow.AddMinutes(-5),
        });

        var tool = SearchFixtures.Tool(jobs, options: new SearchOptions(TimeSpan.FromHours(24), 10, 5000));

        var result = await SearchFixtures.CallAsync(tool, SearchFixtures.ValidRequest());

        var payload = SearchFixtures.Structured(result);
        CollectionAssert.AreEquivalent(
            ContractFields,
            payload.EnumerateObject().Select(property => property.Name).ToList(),
            "A reused result has the same shape as a created one: the caller parses one shape.");
        Assert.AreEqual(existingJobId, payload.GetProperty("job_id").GetGuid());
        Assert.AreEqual("Completed", payload.GetProperty("status").GetString());
        Assert.IsTrue(payload.GetProperty("reused").GetBoolean());
        Assert.AreEqual(
            SearchOptions.DefaultPollAfterMs,
            payload.GetProperty("poll_after_ms").GetInt32(),
            "The default poll hint is 5000 ms.");
    }

    [TestMethod]
    public async Task SearchTours_WhenCalled_EmitsSnakeCaseFieldsAndAStringStatus()
    {
        var jobs = new RecordingJobRepository();
        var tool = SearchFixtures.Tool(jobs);

        var result = await SearchFixtures.CallAsync(tool, SearchFixtures.ValidRequest());

        // The structured payload is JSON on the wire: the names and the enum encoding are what an
        // agent sees, not the C# property names.
        var json = SearchFixtures.Structured(result).GetRawText();

        StringAssert.Contains(json, "\"job_id\"");
        StringAssert.Contains(json, "\"parameters_hash\"");
        StringAssert.Contains(json, "\"poll_after_ms\"");
        StringAssert.Contains(json, "\"status\":\"Pending\"");

        var jobId = JsonDocument
            .Parse(json)
            .RootElement
            .GetProperty("job_id")
            .GetString();

        Assert.IsTrue(
            Guid.TryParseExact(jobId, "D", out _),
            $"job_id is a UUID string on the wire, found '{jobId}' (invariant formatting, no braces).");

        Assert.AreEqual(
            jobs.Created[0].Id.ToString("D", CultureInfo.InvariantCulture),
            jobId,
            "The identifier on the wire is the same value the row holds.");
    }
}
