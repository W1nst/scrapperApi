namespace TourSearch.Core.Entities;

/// <summary>
/// A tour-search task: the unit of work a worker claims, executes and reports on.
/// </summary>
/// <remarks>
/// Plain POCO on purpose — all persistence configuration lives in
/// <c>TourSearch.Infrastructure</c> (flush API, not attributes), so this assembly keeps
/// depending on nothing but the BCL (docs/architecture.md).
///
/// Every <see cref="DateTime"/> here is UTC: Npgsql throws on a non-UTC value instead of
/// converting it silently (docs/database.md#utc-trap).
///
/// No status transition and no validation lives on this type: statuses are written by the
/// worker's claim/recovery pass, and parameter ranges are rejected by the MCP tool layer
/// before a job exists (docs/domain.md#error-taxonomy).
/// </remarks>
public class SearchJob
{
    /// <summary>Primary key. Never order or paginate by it — see docs/domain.md#guid-ordering.</summary>
    public Guid Id { get; set; }

    /// <summary>Canonical hash of <see cref="Parameters"/> — the dedupe/idempotency key.</summary>
    public string ParametersHash { get; set; } = "";

    /// <summary>The requested search parameters, stored as a jsonb column on this table.</summary>
    public SearchParameters Parameters { get; set; } = null!;

    /// <summary>ISO-4217 code of the price band. docs/domain.md#currency: RUB for now.</summary>
    public string CurrencyCode { get; set; } = "RUB";

    /// <summary>Where the job is in its lifecycle.</summary>
    public Enums.JobStatus Status { get; set; }

    /// <summary>How many times a worker has claimed this job.</summary>
    public int Attempt { get; set; }

    /// <summary>Which worker owns the <c>InProgress</c> status — the concurrency guard.</summary>
    public string? LeaseToken { get; set; }

    /// <summary>UTC. When the lease stops being valid; an expired lease is recoverable.</summary>
    public DateTime? LeaseExpiresAt { get; set; }

    /// <summary>UTC. Cancel requested while the job was already <c>InProgress</c>.</summary>
    public DateTime? CancelRequestedAt { get; set; }

    /// <summary>UTC. When the job was created — the ordering column for job pagination.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC. When a worker claimed the job.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>UTC. When a terminal status was reached.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary><c>Completed</c> but partial: some pages or fields failed to parse.</summary>
    public bool Degraded { get; set; }

    /// <summary>
    /// Non-fatal parse problems. Mapped to a native PostgreSQL <c>text[]</c> column
    /// (docs/database.md#warnings-column-representation).
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Running counter of persisted offers; authoritative once a terminal status is reached,
    /// so the caller can judge the result without loading it.
    /// </summary>
    public int OffersCount { get; set; }

    /// <summary>Running counter of pages parsed successfully.</summary>
    public int PagesDone { get; set; }

    /// <summary>Expected page count; <c>null</c> when the source reports no total.</summary>
    public int? PagesTotal { get; set; }

    /// <summary>Machine-readable failure code — see docs/domain.md#error-taxonomy.</summary>
    public Enums.JobErrorCode? ErrorCode { get; set; }

    /// <summary>Human-readable failure message, for the caller.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Whether the failure is transient. <c>null</c> while the job has not failed.</summary>
    public bool? Retryable { get; set; }

    /// <summary>
    /// Reverse foreign-key accessor only — it is not a column. The offers of this job
    /// follow it on delete (cascade), see docs/database.md#cleanup-ttl.
    /// </summary>
    public List<TourOffer> Offers { get; set; } = new();
}
