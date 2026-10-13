namespace TourSearch.Core.Enums;

/// <summary>
/// Lifecycle status of a <see cref="Entities.SearchJob"/>.
/// </summary>
/// <remarks>
/// Persisted as <c>character varying(20)</c> holding the member name verbatim
/// (<c>"InProgress"</c>), see docs/database.md. Declaration order is the documented
/// order in docs/domain.md#statuses-searchjob; no status is written by the model.
/// </remarks>
public enum JobStatus
{
    /// <summary>Created, waiting for a worker to claim it.</summary>
    Pending,

    /// <summary>Claimed: lease token, lease expiry, started-at and attempt are set.</summary>
    InProgress,

    /// <summary>Finished. The result may be partial (see <c>Degraded</c>) or legitimately empty.</summary>
    Completed,

    /// <summary>Terminal error — carries error code, message and retryability.</summary>
    Failed,

    /// <summary>The caller no longer wants the results.</summary>
    Cancelled
}
