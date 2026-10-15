using Microsoft.EntityFrameworkCore;
using TourSearch.Api;
using TourSearch.Core.Entities;
using TourSearch.Core.Enums;
using TourSearch.Core.Search;
using TourSearch.Infrastructure;
using TourSearch.Infrastructure.Jobs;

namespace TourSearch.UnitTests.Search;

/// <summary>
/// Guards FR-003 (dedupe reuses a recent <c>Completed</c> job and nothing else) and FR-004 (the
/// global active-job limit is computed, is a tool-level <c>RATE_LIMITED</c>, and never blocks a
/// reuse).
/// </summary>
/// <remarks>
/// Two halves. The tool's half — the window it passes, the order it checks reuse and the limit in —
/// is driven through the tool over a recording double. The repository's half — which rows are
/// eligible at all — is asserted against the real repository's generated statement, because "only
/// <c>Completed</c>" and "inside the window" are SQL filters that no double can prove. No test here
/// opens a connection (docs/conventions.md#testing).
/// </remarks>
[TestClass]
public sealed class SearchDedupeTests
{
    private const string PlaceholderConnectionString = "Host=localhost;Database=toursearch";

    [TestMethod]
    public async Task SearchTours_WhenACompletedJobIsInsideTheWindow_ReturnsItAndCreatesNothing()
    {
        var existingJobId = Guid.NewGuid();
        var reusableJob = new SearchJob
        {
            Id = existingJobId,
            Status = JobStatus.Completed,
            CompletedAt = DateTime.UtcNow.AddHours(-1),
        };

        var jobs = new RecordingJobRepository(reusableJob) { ActiveJobCount = 0 };
        var tool = SearchFixtures.Tool(jobs);
        var request = SearchFixtures.ValidRequest();
        var before = DateTime.UtcNow;

        var result = await SearchFixtures.CallAsync(tool, request);

        var payload = SearchFixtures.Structured(result);

        Assert.IsFalse(result.IsError ?? false, "Reuse is a normal result, not an error.");
        Assert.IsTrue(payload.GetProperty("reused").GetBoolean(), "An existing identical result was returned.");
        Assert.AreEqual(existingJobId, payload.GetProperty("job_id").GetGuid(), "The caller polls the existing job.");
        Assert.AreEqual(
            "Completed",
            payload.GetProperty("status").GetString(),
            "A reused job reports its own status, not Pending: the caller must not wait for a result that exists.");
        Assert.IsEmpty(jobs.Created, "A reused call creates no job — that is the whole point of dedupe.");

        Assert.AreEqual(
            ParameterHasher.ComputeHash(request),
            jobs.LastParametersHash,
            "Dedupe looks up the canonical hash of the request (FR-002).");

        Assert.IsNotNull(jobs.LastCompletedNotBeforeUtc, "The lookup must be bounded in time, or it would reuse forever.");
        var cutoff = jobs.LastCompletedNotBeforeUtc.Value;
        Assert.AreEqual(
            DateTimeKind.Utc,
            cutoff.Kind,
            "The window bound is a UTC instant; Npgsql rejects a non-UTC timestamp (docs/database.md#utc-trap).");
        Assert.IsTrue(
            cutoff >= before.AddHours(-24) && cutoff <= DateTime.UtcNow.AddHours(-24),
            $"The bound must be 'now minus the reuse window' (24h by default), found {cutoff:O}.");
    }

    [TestMethod]
    public async Task SearchTours_WhenTheReuseWindowIsConfigured_UsesTheConfiguredWindow()
    {
        var jobs = new RecordingJobRepository();
        var tool = SearchFixtures.Tool(jobs, options: new SearchOptions(TimeSpan.FromHours(2), 10, 5000));
        var before = DateTime.UtcNow;

        await SearchFixtures.CallAsync(tool, SearchFixtures.ValidRequest());

        var cutoff = jobs.LastCompletedNotBeforeUtc!.Value;
        Assert.IsTrue(
            cutoff >= before.AddHours(-2) && cutoff <= DateTime.UtcNow.AddHours(-2),
            $"The window is a setting, not a constant: a 2-hour window must produce a 2-hour bound, found {cutoff:O}.");
    }

    [TestMethod]
    public async Task SearchTours_WhenThereIsNoEligibleCompletedJob_CreatesANewOne()
    {
        var jobs = new RecordingJobRepository(reusableCompletedJob: null) { ActiveJobCount = 0 };
        var tool = SearchFixtures.Tool(jobs);

        var result = await SearchFixtures.CallAsync(tool, SearchFixtures.ValidRequest());

        var payload = SearchFixtures.Structured(result);

        Assert.IsFalse(payload.GetProperty("reused").GetBoolean(), "Nothing eligible was found, so nothing was reused.");
        Assert.HasCount(1, jobs.Created, "A new job is created instead.");
        Assert.AreEqual(
            jobs.Created[0].Id,
            payload.GetProperty("job_id").GetGuid(),
            "The caller polls the job that was just created.");
        Assert.AreEqual("Pending", payload.GetProperty("status").GetString());
    }

    [TestMethod]
    public void BuildReusableStatement_WhenBuilt_FiltersOnCompletedInsideTheWindowNewestFirst()
    {
        using var context = CreateContext();
        var repository = new JobRepository(context);
        var cutoff = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

        var sql = repository.BuildReusableStatement("sha256:abc", cutoff);

        StringAssert.Contains(sql, "tour_search.search_jobs", "The lookup reads the job queue.");
        StringAssert.Contains(
            sql,
            "s.status = 'Completed'",
            "Only a Completed job may be reused (docs/domain.md#identity-and-dedupe). Statement:\n" + sql);
        StringAssert.Contains(
            sql,
            "parameters_hash = @parametersHash",
            "It looks up the canonical hash (ADR 012).");
        StringAssert.Contains(
            sql,
            "s.completed_at IS NOT NULL",
            "A Completed job without CompletedAt cannot be placed on the freshness timeline.");
        StringAssert.Contains(
            sql,
            "s.completed_at >= @completedNotBeforeUtc",
            "The reuse window is applied by the database, not filtered in memory afterwards (ADR 047).");
        StringAssert.Contains(
            sql,
            "ORDER BY s.completed_at DESC",
            "The newest eligible job is the one returned.");

        // A Pending, InProgress, Failed or Cancelled job is never reused: this task leaves the
        // InProgress/Failed questions open rather than deciding them in passing, and a Cancelled job
        // must not shadow a completed one.
        foreach (var status in new[] { "Pending", "InProgress", "Failed", "Cancelled" })
        {
            Assert.IsFalse(
                sql.Contains($"'{status}'", StringComparison.Ordinal),
                $"'{status}' must not appear in the reuse lookup, or that status would be reused. " +
                $"Statement:\n{sql}");
        }
    }

    [TestMethod]
    public void BuildActiveCountStatement_WhenBuilt_CountsOnlyWorkInFlight()
    {
        using var context = CreateContext();
        var repository = new JobRepository(context);

        var sql = repository.BuildActiveCountStatement();

        // The count itself is EF's aggregate over this predicate, added at execution — what a unit
        // test can and must pin is the predicate. That the count is COMPUTED rather than read from a
        // stored counter is structural: search_jobs has no counter column, which ModelShapeTests
        // asserts column by column (docs/mcp-tools.md#rate-limiting).
        StringAssert.Contains(sql, "tour_search.search_jobs");
        StringAssert.Contains(
            sql,
            "s.status IN ('Pending', 'InProgress')",
            "The equality column comes first and only work in flight counts. Statement:\n" + sql);

        foreach (var status in new[] { "Completed", "Failed", "Cancelled" })
        {
            Assert.IsFalse(
                sql.Contains(status, StringComparison.Ordinal),
                $"A finished job is not work in flight and must not count against the limit. " +
                $"Statement:\n{sql}");
        }
    }

    [TestMethod]
    public async Task SearchTours_WhenTheLimitIsReached_RejectsWithRateLimitedAndCreatesNothing()
    {
        var jobs = new RecordingJobRepository { ActiveJobCount = 10 };
        var tool = SearchFixtures.Tool(jobs, options: new SearchOptions(TimeSpan.FromHours(24), 10, 5000));

        var result = await SearchFixtures.CallAsync(tool, SearchFixtures.ValidRequest());

        Assert.IsTrue(
            result.IsError ?? false,
            "Hitting the limit is a tool-level error, not a Failed job: no job exists (FR-004).");

        var payload = SearchFixtures.Structured(result);

        Assert.AreEqual("RATE_LIMITED", payload.GetProperty("error_code").GetString());
        Assert.IsFalse(
            payload.TryGetProperty("field", out _),
            "No parameter is at fault, so no field may be named: naming one would send the caller to " +
            "fix a request that is not wrong.");

        Assert.IsEmpty(jobs.Created, "A rejected request creates no row.");

        var message = payload.GetProperty("message").GetString();
        Assert.IsNotNull(message);
        StringAssert.Contains(message, "RATE_LIMITED");
        StringAssert.Contains(
            message,
            "GLOBAL",
            "The limit is documented as global until caller identity exists: presenting it as " +
            "per-caller would be a lie (docs/mcp-tools.md#rate-limiting).");
    }

    [TestMethod]
    [DataRow(1, 1, DisplayName = "limit 1, one active job")]
    [DataRow(3, 3, DisplayName = "limit 3, three active jobs")]
    [DataRow(3, 7, DisplayName = "limit 3, seven active jobs")]
    public async Task SearchTours_WhenTheConfiguredLimitIsReached_StopsAtThatLimit(int limit, int activeJobs)
    {
        var jobs = new RecordingJobRepository { ActiveJobCount = activeJobs };
        var tool = SearchFixtures.Tool(jobs, options: new SearchOptions(TimeSpan.FromHours(24), limit, 5000));

        var result = await SearchFixtures.CallAsync(tool, SearchFixtures.ValidRequest());

        Assert.AreEqual(
            "RATE_LIMITED",
            SearchFixtures.Structured(result).GetProperty("error_code").GetString(),
            $"With the limit configured to {limit} and {activeJobs} jobs active, no job may be created.");
        Assert.IsEmpty(jobs.Created);
    }

    [TestMethod]
    public async Task SearchTours_WhenTheActiveJobsAreBelowTheLimit_CreatesTheJob()
    {
        var jobs = new RecordingJobRepository { ActiveJobCount = 9 };
        var tool = SearchFixtures.Tool(jobs, options: new SearchOptions(TimeSpan.FromHours(24), 10, 5000));

        var result = await SearchFixtures.CallAsync(tool, SearchFixtures.ValidRequest());

        Assert.IsFalse(result.IsError ?? false, "Nine active jobs are below a limit of ten.");
        Assert.HasCount(1, jobs.Created);
    }

    [TestMethod]
    public async Task SearchTours_WhenTheLimitIsReachedButTheRequestWouldReuse_StillReusesTheJob()
    {
        var existingJobId = Guid.NewGuid();
        var jobs = new RecordingJobRepository(new SearchJob
        {
            Id = existingJobId,
            Status = JobStatus.Completed,
            CompletedAt = DateTime.UtcNow,
        })
        {
            ActiveJobCount = 10,
        };

        var tool = SearchFixtures.Tool(jobs, options: new SearchOptions(TimeSpan.FromHours(24), 10, 5000));

        var result = await SearchFixtures.CallAsync(tool, SearchFixtures.ValidRequest());

        var payload = SearchFixtures.Structured(result);

        Assert.IsFalse(
            result.IsError ?? false,
            "Reuse is checked BEFORE the limit: a reused job creates no work, so the cap it does not " +
            "consume must never reject it (NFR-002).");

        Assert.IsTrue(payload.GetProperty("reused").GetBoolean());
        Assert.AreEqual(existingJobId, payload.GetProperty("job_id").GetGuid());
        Assert.AreEqual(
            0,
            jobs.CountActiveCalls,
            "The limit check must not even run when the request is served by reuse.");
    }

    [TestMethod]
    public async Task SearchTours_WhenTheRequestIsNew_CountsTheActiveJobsBeforeInserting()
    {
        var jobs = new RecordingJobRepository { ActiveJobCount = 0 };
        var tool = SearchFixtures.Tool(jobs);

        await SearchFixtures.CallAsync(tool, SearchFixtures.ValidRequest());

        Assert.AreEqual(
            1,
            jobs.CountActiveCalls,
            "Exactly one computed count per call: never a stored counter, and never a second query.");
    }

    private static TourSearchDbContext CreateContext() => new(
        new DbContextOptionsBuilder<TourSearchDbContext>()
            .UseNpgsql(PlaceholderConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
}
