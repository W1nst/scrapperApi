// TourSearch.Api — the MCP server (Streamable HTTP) and the HTTP surface.
//
// The process serves two endpoints:
//   GET  /health  -> server metadata (docs/runtime.md#ports-and-health)
//   POST /mcp     -> MCP over Streamable HTTP, stateless (docs/mcp-tools.md)
//
// The DI graph is AddInfrastructure() plus the MCP server. This process registers no
// BackgroundService (ADR 004) and applies no migration at startup (ADR 041); it must not
// reference TourSearch.Scraper (ADR 001). search_tours writes a Pending job row and returns —
// nothing claims or executes it, and get_search_results is still an honest stub that names its
// missing prerequisite instead of inventing data (docs/adr/044-tools-ship-as-stubs-first.md).
//
// Exit codes (docs/runtime.md#shutdown) are returned from Main, not set with Environment.Exit
// deep in the stack, so console flushing and host disposal stay ordered:
//   0 = clean shutdown, 1 = bad configuration, 2 = unrecoverable internal error.

using TourSearch.Api;

return await ApiProgram.RunAsync(args);
