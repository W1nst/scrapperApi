using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using TourSearch.Core.Enums;

namespace TourSearch.Api.Tools;

/// <summary>
/// Builds the <b>tool-level error</b> results of the MCP tools
/// (docs/mcp-tools.md#errors--two-different-layers).
/// </summary>
/// <remarks>
/// <para>
/// A rejected call is a <b>result</b> with <c>isError: true</c>, never a JSON-RPC <c>error</c>
/// object: <c>-32602</c> means "malformed request" (an unknown tool, an argument of the wrong
/// type), not "<c>adults</c> must be at least 1". Encoding a parameter problem as a protocol error
/// tells the agent the transport is broken, which is false and unfixable from its side.
/// </para>
/// <para>
/// The machine-readable part is <c>structuredContent</c> — <c>error_code</c>, and the offending
/// <c>field</c> when a single parameter is at fault — so an agent can correct itself without
/// parsing the prose in the text block. A code without a field (<c>RATE_LIMITED</c>) is not
/// repaired by changing the request, and naming a field there would send the agent to fix the
/// wrong thing.
/// </para>
/// <para>
/// These codes are never persisted: no row exists to carry them, which is why
/// <c>PARAM_INVALID</c>, <c>JOB_NOT_FOUND</c> and <c>RATE_LIMITED</c> are the tool-level members of
/// the taxonomy (docs/domain.md#error-taxonomy).
/// </para>
/// </remarks>
internal static class ToolErrors
{
    /// <summary>Builds a <c>PARAM_INVALID</c> result naming the offending field.</summary>
    /// <param name="field">The contract's field name, e.g. <c>adults</c> or <c>start_date</c>.</param>
    /// <param name="message">The human-readable rule that failed; also the text block.</param>
    /// <returns>An error result with <c>error_code</c>, <c>field</c> and the message.</returns>
    internal static CallToolResult ParamInvalid(string field, string message) =>
        Error(JobErrorCode.PARAM_INVALID, field, message);

    /// <summary>Builds a <c>RATE_LIMITED</c> result — no field is at fault.</summary>
    /// <param name="message">The human-readable explanation, including the global limit; also the text block.</param>
    /// <returns>An error result with <c>error_code</c> and the message, and no <c>field</c>.</returns>
    internal static CallToolResult RateLimited(string message) =>
        Error(JobErrorCode.RATE_LIMITED, field: null, message);

    private static CallToolResult Error(JobErrorCode errorCode, string? field, string message)
    {
        var payload = new JsonObject
        {
            ["error_code"] = errorCode.ToString(),
        };

        if (field is not null)
        {
            payload["field"] = field;
        }

        payload["message"] = message;

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],

            // The same serializer options the SDK uses for the protocol itself: a payload built with
            // different options would drift from the rest of the server's JSON.
            StructuredContent = JsonSerializer.SerializeToElement(payload, McpJsonUtilities.DefaultOptions),
            IsError = true,
        };
    }
}
