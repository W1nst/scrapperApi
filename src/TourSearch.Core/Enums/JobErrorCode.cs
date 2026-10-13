namespace TourSearch.Core.Enums;

/// <summary>
/// Machine-readable failure code of a <see cref="Entities.SearchJob"/>, so a caller can
/// decide whether a retry is worth attempting; the human-readable counterpart is
/// <c>SearchJob.ErrorMessage</c>.
/// </summary>
/// <remarks>
/// The taxonomy and its retryability are fixed by docs/domain.md#error-taxonomy. Persisted
/// as <c>character varying(40)</c> holding the member name verbatim (<c>"NETWORK_TIMEOUT"</c>).
/// </remarks>
public enum JobErrorCode
{
    /// <summary>Navigation exceeded the timeout. Retryable.</summary>
    NETWORK_TIMEOUT,

    /// <summary>DNS/TLS/connection failure. Retryable.</summary>
    NETWORK_UNREACHABLE,

    /// <summary>Source throttling — back off harder. Retryable.</summary>
    HTTP_429,

    /// <summary>Source-side failure. Retryable.</summary>
    HTTP_5XX,

    /// <summary>Playwright/Chromium died. Retryable.</summary>
    BROWSER_CRASH,

    /// <summary>The worker died mid-job. Retryable.</summary>
    LEASE_EXPIRED,

    /// <summary>A required selector is missing — the markup changed. Not retryable.</summary>
    SELECTOR_NOT_FOUND,

    /// <summary>The item container parsed but its fields did not. Not retryable.</summary>
    PAGE_STRUCTURE_CHANGED,

    /// <summary>CAPTCHA or block page. Not retryable.</summary>
    ANTI_BOT_DETECTED,

    /// <summary>Rejected before the job exists — tool-level only, never persisted.</summary>
    PARAM_INVALID,

    /// <summary>Unknown or foreign job id — tool-level only, never persisted.</summary>
    JOB_NOT_FOUND,

    /// <summary>A bug. Retry only after a fix.</summary>
    INTERNAL
}
