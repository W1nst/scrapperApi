using System.Text.Json.Serialization;
using TourSearch.Core.Enums;

namespace TourSearch.Api.Tools.Contracts;

/// <summary>
/// The result of <c>search_tours</c> (docs/mcp-tools.md#search_tours): the job that was created,
/// or the existing completed job that was reused.
/// </summary>
/// <param name="JobId">The identifier to poll with <c>get_search_results</c>.</param>
/// <param name="Status">The status of the job at the moment it was returned — normally <c>Pending</c>.</param>
/// <param name="Reused">
/// <see langword="true"/> when an existing completed job with the same parameter hash was
/// returned instead of a new job being started (docs/domain.md#identity-and-dedupe).
/// </param>
/// <param name="ParametersHash">The canonical hash of the search parameters; the server-derived dedupe key.</param>
/// <param name="PollAfterMs">
/// The server's hint for the first poll. The caller must not poll tighter than this.
/// </param>
public sealed record SearchStartedResponse(
    [property: JsonPropertyName("job_id")] Guid JobId,
    [property: JsonPropertyName("status")] JobStatus Status,
    [property: JsonPropertyName("reused")] bool Reused,
    [property: JsonPropertyName("parameters_hash")] string ParametersHash,
    [property: JsonPropertyName("poll_after_ms")] int PollAfterMs);
