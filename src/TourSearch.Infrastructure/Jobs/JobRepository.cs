using Microsoft.EntityFrameworkCore;
using TourSearch.Core.Entities;
using TourSearch.Core.Enums;

namespace TourSearch.Infrastructure.Jobs;

/// <summary>
/// Reads and writes the job queue through the one <see cref="TourSearchDbContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered <c>Scoped</c>: it resolves the request's context and holds no state of its own
/// (docs/conventions.md#di).
/// </para>
/// <para>
/// The reuse lookup and the active count are the two queries <c>search_tours</c> runs before it
/// writes anything. Both are exposed as statements as well, so a unit test can assert the
/// <b>filter</b> they apply without a database: the reuse window and the "only <c>Completed</c>"
/// rule are what make dedupe correct, and neither is visible in the tool's code.
/// </para>
/// </remarks>
public sealed class JobRepository : IJobRepository
{
    private readonly TourSearchDbContext _context;

    /// <summary>Creates the repository over the context of the current scope.</summary>
    /// <param name="context">The scope's <see cref="TourSearchDbContext"/>; never disposed here.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public JobRepository(TourSearchDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task<SearchJob?> FindReusableCompletedAsync(
        string parametersHash,
        DateTime completedNotBeforeUtc,
        CancellationToken cancellationToken)
    {
        return await BuildReusableQuery(parametersHash, completedNotBeforeUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> CountActiveAsync(CancellationToken cancellationToken)
    {
        return await BuildActiveCountQuery()
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CreateAsync(SearchJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        _context.SearchJobs.Add(job);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the SQL of the reuse lookup for the given window, without running it.
    /// </summary>
    /// <param name="parametersHash">The same hash <see cref="FindReusableCompletedAsync"/> accepts.</param>
    /// <param name="completedNotBeforeUtc">The same inclusive UTC lower bound.</param>
    /// <returns>The statement the lookup translates to.</returns>
    /// <remarks>
    /// Building a command opens no connection, so this is how a test asserts the lookup's filter —
    /// <c>status = 'Completed'</c>, the hash, the window, newest first — without a database.
    /// </remarks>
    public string BuildReusableStatement(string parametersHash, DateTime completedNotBeforeUtc) =>
        BuildReusableQuery(parametersHash, completedNotBeforeUtc).ToQueryString();

    /// <summary>Returns the SQL of the active-job count, without running it.</summary>
    /// <returns>The statement the count translates to.</returns>
    public string BuildActiveCountStatement() => BuildActiveCountQuery().ToQueryString();

    /// <summary>
    /// Builds the reuse lookup: the hash, only <c>Completed</c> jobs, completed inside the window,
    /// most recent first.
    /// </summary>
    /// <param name="parametersHash">The canonical hash of the request.</param>
    /// <param name="completedNotBeforeUtc">The inclusive UTC lower bound.</param>
    /// <returns>The composed query, executed by <see cref="FindReusableCompletedAsync"/>.</returns>
    private IQueryable<SearchJob> BuildReusableQuery(string parametersHash, DateTime completedNotBeforeUtc)
    {
        // AsNoTracking: the returned job is read for its id and status only and is never written
        // back, so the scope's change tracker has no business holding it
        // (docs/database.md#conventions).
        return _context.SearchJobs
            .AsNoTracking()
            .Where(job =>
                job.Status == JobStatus.Completed
                && job.ParametersHash == parametersHash

                // Defensive: a Completed job without CompletedAt cannot be placed on the freshness
                // timeline, so it is not reused rather than being reused forever.
                && job.CompletedAt != null
                && job.CompletedAt >= completedNotBeforeUtc)

            // The most recent eligible job, regardless of other rows: a Cancelled job of the same
            // hash does not shadow the Completed one (task 006, open questions).
            .OrderByDescending(job => job.CompletedAt);
    }

    /// <summary>
    /// Builds the active-job count: <c>Pending</c> or <c>InProgress</c>, and nothing else.
    /// </summary>
    /// <returns>The composed query, executed by <see cref="CountActiveAsync"/>.</returns>
    private IQueryable<SearchJob> BuildActiveCountQuery() => _context.SearchJobs
        .AsNoTracking()

        // A job that reached a terminal status is not work in flight: only work in flight consumes
        // the limit, and the predicate is served by ix_search_jobs_status_created_at
        // (docs/database.md#index-rules).
        .Where(job => job.Status == JobStatus.Pending || job.Status == JobStatus.InProgress);
}
