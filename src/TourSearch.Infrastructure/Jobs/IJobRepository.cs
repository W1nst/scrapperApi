using TourSearch.Core.Entities;

namespace TourSearch.Infrastructure.Jobs;

/// <summary>
/// The write path of the job queue as far as <c>search_tours</c> needs it: find a reusable
/// <c>Completed</c> job, count the active ones, and create a <c>Pending</c> one.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately <b>not</b> the worker's queue: claiming a job is an atomic compare-and-set
/// that only the Worker performs (ADR 005, docs/database.md#job-claim-mechanism). Nothing here
/// transitions a job out of <c>Pending</c>, increments <c>Attempt</c>, or writes a lease.
/// </para>
/// <para>
/// Every <see cref="DateTime"/> entering or leaving is UTC — Npgsql rejects a <c>Local</c> or
/// <c>Unspecified</c> value instead of converting it (docs/database.md#utc-trap).
/// </para>
/// <para>
/// Implementations are registered <c>Scoped</c>: they are stateless and must see the request's own
/// context and its cancellation token (docs/conventions.md#di).
/// </para>
/// </remarks>
public interface IJobRepository
{
    /// <summary>
    /// Finds the most recently completed job with this parameter hash, if it completed inside the
    /// reuse window.
    /// </summary>
    /// <param name="parametersHash">The canonical hash of the request; never empty.</param>
    /// <param name="completedNotBeforeUtc">
    /// UTC lower bound of the reuse window, inclusive (<c>now - reuse window</c>). A job that
    /// completed before it is <b>not</b> reusable: freshness is not retention
    /// (docs/adr/047-reuse-window-separate-from-ttl.md).
    /// </param>
    /// <param name="cancellationToken">The caller's token, forwarded to the query.</param>
    /// <returns>
    /// The reusable job, or <see langword="null"/> when there is none. Only <c>Completed</c> jobs are
    /// ever returned: <c>Pending</c>, <c>InProgress</c>, <c>Failed</c> and <c>Cancelled</c> jobs are
    /// never reused (docs/domain.md#identity-and-dedupe).
    /// </returns>
    Task<SearchJob?> FindReusableCompletedAsync(
        string parametersHash,
        DateTime completedNotBeforeUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Counts the jobs that are <c>Pending</c> or <c>InProgress</c> — the global load-shedding
    /// guard's input.
    /// </summary>
    /// <param name="cancellationToken">The caller's token, forwarded to the query.</param>
    /// <returns>
    /// The number of active jobs. It is <b>computed</b> on every call, never read from a stored
    /// counter: a counter would need the same CAS discipline as the job claim and would drift on
    /// every crash (docs/mcp-tools.md#rate-limiting).
    /// </returns>
    /// <remarks>
    /// The count is global, not per caller — there is no caller identity yet
    /// (docs/open-questions.md).
    /// </remarks>
    Task<int> CountActiveAsync(CancellationToken cancellationToken);

    /// <summary>Inserts a new job exactly as given, without claiming it.</summary>
    /// <param name="job">
    /// The job to persist. It must be <c>Pending</c> with no lease: claiming is the Worker's
    /// compare-and-set (ADR 005).
    /// </param>
    /// <param name="cancellationToken">The caller's token, forwarded to the insert.</param>
    /// <returns>A task that completes when the row is committed.</returns>
    /// <remarks>
    /// A single EF operation, so cancellation either commits the row or does not create it — no
    /// half-written job is left behind (docs/conventions.md#cancellation).
    /// </remarks>
    Task CreateAsync(SearchJob job, CancellationToken cancellationToken);
}
