// Placeholder entry point — it exists only so the skeleton builds.
//
// The real startup sequence is specified in docs/runtime.md#startup and is
// deliberately NOT implemented yet: AGENTS.md §2 blocks implementation code
// until the ADRs for the [DECIDED] items and the blocking questions in
// docs/open-questions.md are resolved. Replace this file when that gate opens;
// do not grow it.
//
// The MCP endpoint is Streamable HTTP (docs/mcp-tools.md), not the stdio
// transport that `dotnet new mcpserver` generates by default.
//
// Exit codes are defined in docs/runtime.md#shutdown:
//   0 = clean shutdown, 1 = bad configuration, 2 = unrecoverable internal error.

System.Console.Error.WriteLine("TourSearch.Api is not implemented; see docs/runtime.md#startup and AGENTS.md §2.");
return 1;
