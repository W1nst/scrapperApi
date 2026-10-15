using ModelContextProtocol.Protocol;

namespace TourSearch.Api.Tools;

/// <summary>
/// Builds the result of a tool that is declared but not implemented yet.
/// </summary>
/// <remarks>
/// <para>
/// An honest stub is a <b>normal result</b>, not a JSON-RPC error: the transport worked, the tool
/// ran, and the answer is "not available yet". It is also not an empty success and not a
/// plausible default — those are indistinguishable from real data and are exactly the confusion
/// ADR 010 exists to prevent (docs/adr/044-tools-ship-as-stubs-first.md).
/// </para>
/// <para>
/// The text lives in the tool <b>response</b>, not only in a log line: the caller is an agent, and
/// the agent is the one that must not mistake the stub for data.
/// </para>
/// </remarks>
internal static class StubResult
{
    /// <summary>Builds a "not implemented" tool result carrying <paramref name="explanation"/>.</summary>
    /// <param name="explanation">
    /// What is missing, which prerequisite it needs, and what deliberately did not happen.
    /// </param>
    /// <returns>A normal, non-error tool result whose text is the explanation.</returns>
    internal static CallToolResult NotImplemented(string explanation) => new()
    {
        Content = [new TextContentBlock { Text = explanation }],
        IsError = false,

        // No StructuredContent: the declared outputSchema describes data this tool cannot produce
        // yet, and returning it empty or defaulted would fabricate a result.
    };
}
