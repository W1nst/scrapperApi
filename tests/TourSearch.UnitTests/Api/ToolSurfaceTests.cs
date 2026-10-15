using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TourSearch.Api;
using TourSearch.Api.Tools;
using TourSearch.Infrastructure;

namespace TourSearch.UnitTests.Api;

/// <summary>
/// Guards the MCP surface of <c>TourSearch.Api</c> against <c>docs/mcp-tools.md</c>: the tool
/// set, the argument names, the <c>outputSchema</c> of every tool, the descriptions that carry the
/// contract's rules, the server metadata, and the absence of a TourSearch hosted service.
/// </summary>
/// <remarks>
/// <para>
/// No HTTP server is started and nothing is contacted (NFR-001, NFR-003): every assertion reads
/// the MCP graph the API process builds. A drifted tool name, a camelCase schema field or an
/// accidentally added fourth tool fails here instead of being discovered by an agent at runtime.
/// </para>
/// <para>
/// <c>AddTourSearchApi()</c> calls <c>AddInfrastructure()</c>, which reads the process-wide
/// <c>TOURSEARCH_DB_*</c> environment, so this class is not parallelized. No value written here is
/// a credential and none of them is a literal password: it is generated per run, because a
/// password-shaped constant in a committed file is a leak even when it is obviously fake
/// (ADR 003). Registering the graph opens no database connection.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ToolSurfaceTests
{
    private const string ContractMcpPath = "/mcp";
    private const string ContractProtocolRevision = "2026-07-28";

    private static readonly string[] ContractToolNames =
    [
        "list_regions",
        "search_tours",
        "get_search_results",
    ];

    private readonly Dictionary<string, string?> _previousValues = new();

    /// <summary>Sets the documented variables, so the API graph can be built.</summary>
    [TestInitialize]
    public void SetTheDocumentedVariables()
    {
        SetVariable(DbConnectionSettings.HostVariable, "127.0.0.1");
        SetVariable(DbConnectionSettings.PortVariable, "5432");
        SetVariable(DbConnectionSettings.NameVariable, "tour_search");
        SetVariable(DbConnectionSettings.UserVariable, "postgres");
        SetVariable(DbConnectionSettings.PasswordVariable, Guid.NewGuid().ToString("N"));
    }

    /// <summary>Puts the process environment back the way the test found it.</summary>
    [TestCleanup]
    public void RestoreTheEnvironment()
    {
        foreach (var (name, value) in _previousValues)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        _previousValues.Clear();
    }

    [TestMethod]
    public void RegisteredTools_WhenTheApiGraphIsBuilt_AreExactlyTheThreeContractTools()
    {
        var options = BuildMcpServerOptions();

        CollectionAssert.AreEquivalent(
            ContractToolNames,
            Tools(options).PrimitiveNames.ToList(),
            "docs/mcp-tools.md ships exactly three tools. A fourth tool (for example " +
            "list_recent_searches, which is blocked on auth) or a missing one is a contract change.");
    }

    [TestMethod]
    [DataRow(
        "list_regions",
        "regions,truncated",
        DisplayName = "list_regions declares exactly {regions, truncated}")]
    [DataRow(
        "search_tours",
        "job_id,status,reused,parameters_hash,poll_after_ms",
        DisplayName = "search_tours declares exactly the job-start fields")]
    [DataRow(
        "get_search_results",
        "status,poll_after_ms,progress,offers,page,degraded,warnings,offers_total,scraped_at,error_code,error_message,retryable,attempt",
        DisplayName = "get_search_results declares the union of the four documented shapes")]
    public void OutputSchema_WhenEmitted_DeclaresExactlyTheContractFields(string toolName, string expectedFields)
    {
        var options = BuildMcpServerOptions();
        var properties = PropertyNames(OutputSchema(options, toolName));

        CollectionAssert.AreEquivalent(
            expectedFields.Split(','),
            properties,
            $"outputSchema of '{toolName}' must match docs/mcp-tools.md field for field " +
            "(snake_case, no invented field, none missing).");
    }

    [TestMethod]
    public void OutputSchema_WhenEmitted_IsDeclaredForEveryTool()
    {
        var options = BuildMcpServerOptions();

        foreach (var name in ContractToolNames)
        {
            var tool = ContractTool(options, name);

            Assert.IsTrue(
                tool.OutputSchema.HasValue,
                $"'{name}' must declare an outputSchema: a missing one makes the agent parse " +
                "free-form text and guess (docs/mcp-tools.md#errors--two-different-layers).");
        }
    }

    [TestMethod]
    public void OutputSchema_WhenEmitted_UsesTheContractNestedFieldNames()
    {
        var options = BuildMcpServerOptions();

        CollectionAssert.AreEquivalent(
            new[] { "id", "label", "offers_count" },
            PropertyNames(OutputSchema(options, "list_regions").GetProperty("properties").GetProperty("regions").GetProperty("items")),
            "list_regions.regions[] fields must be snake_case exactly as documented.");

        var results = OutputSchema(options, "get_search_results").GetProperty("properties");

        CollectionAssert.AreEquivalent(
            new[] { "offers_so_far", "pages_done", "pages_total" },
            PropertyNames(results.GetProperty("progress")),
            "progress maps to the domain counters OffersCount, PagesDone, PagesTotal.");

        CollectionAssert.AreEquivalent(
            new[] { "next_cursor", "has_more" },
            PropertyNames(results.GetProperty("page")),
            "The pagination cursor lives in page, and there is no second offer count (ADR 008).");

        CollectionAssert.AreEquivalent(
            new[]
            {
                "hotel_name", "hotel_stars", "luxury", "region", "check_in", "nights", "price",
                "currency", "room_type", "meal_type", "source_url",
            },
            PropertyNames(results.GetProperty("offers").GetProperty("items")),
            "Offers must carry exactly the documented fields.");
    }

    [TestMethod]
    public void InputSchema_WhenEmitted_UsesTheContractArgumentNames()
    {
        var options = BuildMcpServerOptions();

        var regions = ContractTool(options, "list_regions").InputSchema;
        CollectionAssert.AreEquivalent(new[] { "query" }, PropertyNames(regions));
        Assert.HasCount(0, RequiredNames(regions), "list_regions.query is optional: empty means all regions.");

        // Parameter names must be snake_case: the SDK takes argument names from the C# parameter
        // names, so a camelCase schema would silently ignore the contract's arguments.
        var search = ContractTool(options, "search_tours").InputSchema;
        CollectionAssert.AreEquivalent(
            new[]
            {
                "region", "adults", "children", "hotel_stars", "start_date", "nights", "price_min",
                "price_max", "currency",
            },
            PropertyNames(search));
        CollectionAssert.AreEquivalent(
            new[] { "region", "adults", "start_date", "nights" },
            RequiredNames(search),
            "A search without dates or party size is not a search; the rest carry documented defaults.");

        var results = ContractTool(options, "get_search_results").InputSchema;
        CollectionAssert.AreEquivalent(new[] { "job_id", "limit", "cursor" }, PropertyNames(results));
        CollectionAssert.AreEquivalent(new[] { "job_id" }, RequiredNames(results));
        Assert.AreEqual(
            20,
            results.GetProperty("properties").GetProperty("limit").GetProperty("default").GetInt32(),
            "docs/mcp-tools.md: limit defaults to 20 (maximum 50, documented in the parameter description).");
    }

    [TestMethod]
    public void ToolDescriptions_WhenEmitted_CarryTheContractRules()
    {
        var options = BuildMcpServerOptions();

        var regions = Description(options, "list_regions");
        StringAssert.Contains(regions, "search_tours.region", "The region rule must reach the agent.");
        StringAssert.Contains(regions, "returned by this tool");

        var search = Description(options, "search_tours");
        StringAssert.Contains(search, "NON-BLOCKING");
        StringAssert.Contains(search, "get_search_results");
        StringAssert.Contains(search, "poll_after_ms");

        // The rules an agent cannot discover from the schema: the reuse window, the GLOBAL limit
        // and its code, the parameter error, and the fact that no worker exists in this build
        // (FR-003, FR-004, FR-006).
        StringAssert.Contains(search, "reuse window");
        StringAssert.Contains(search, "GLOBAL");
        StringAssert.Contains(search, "RATE_LIMITED");
        StringAssert.Contains(search, "PARAM_INVALID");
        StringAssert.Contains(
            search,
            "NO WORKER EXISTS IN THIS BUILD",
            "A created job is never picked up, and an agent that is not told will wait forever (FR-006).");

        var results = Description(options, "get_search_results");
        StringAssert.Contains(results, "poll_after_ms");
        StringAssert.Contains(results, "next_cursor");
        StringAssert.Contains(results, "maximum 50");
    }

    [TestMethod]
    public void ServerMetadata_WhenBuilt_NamesTheServerAndCarriesTheContractInstructions()
    {
        var options = BuildMcpServerOptions();

        Assert.IsNotNull(options.ServerInfo, "The server must name itself: MCP clients key on it.");
        Assert.AreEqual("tour-search", options.ServerInfo.Name);
        Assert.AreEqual("0.0.1", options.ServerInfo.Version);

        var instructions = options.ServerInstructions;
        Assert.IsNotNull(instructions, "The contract requires an instructions string.");
        StringAssert.Contains(instructions, "list_regions");
        StringAssert.Contains(instructions, "get_search_results");
        StringAssert.Contains(instructions, "poll_after_ms");
        StringAssert.Contains(instructions, "Never invent a region");

        // The same caveat as the tool description: the Worker does not exist in this build, so a
        // created job never runs and the agent must not wait for it (FR-006).
        StringAssert.Contains(instructions, "NO WORKER EXISTS IN THIS BUILD");
        StringAssert.Contains(
            instructions,
            "GLOBAL",
            "The active-job limit is global until caller identity exists, and must be described as such.");
    }

    [TestMethod]
    public void ApiGraph_WhenBuilt_RegistersNoTourSearchHostedService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTourSearchApi(Configuration());

        // The SDK's own transport infrastructure (ModelContextProtocol.AspNetCore's idle-session
        // tracker) is registered by AddMcpServer() and is not ours; only a TourSearch-owned
        // BackgroundService would mean two processes picking up the same job (ADR 004).
        var ownedHostedServices = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType?.FullName ?? "(factory)")
            .Where(name => name.StartsWith("TourSearch", StringComparison.Ordinal))
            .ToList();

        Assert.HasCount(
            0,
            ownedHostedServices,
            "Only the Worker starts a BackgroundService: the API graph must contain no TourSearch " +
            "hosted service (ADR 004, docs/runtime.md#startup).");
    }

    [TestMethod]
    public void AddTourSearchApi_WhenAnMcpSettingIsMissing_FailsFastNamingTheSetting()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => services.AddTourSearchApi(configuration));

        StringAssert.Contains(
            exception.Message,
            McpEndpointOptions.PathKey,
            "Startup must fail fast naming the missing piece, not half-start (docs/runtime.md#startup).");
    }

    [TestMethod]
    public void StubTool_WhenCalled_ReturnsANormalResultNamingTheMissingPrerequisite()
    {
        // search_tours went live in task 006: it validates, dedupes, enforces the global limit and
        // writes a Pending job. get_search_results is the only stub left — reading a job is a
        // separate task, and a lone implementation of either tool would not be useful to an agent
        // (ADR 044).
        var result = SearchTools.GetSearchResults(
            job_id: Guid.NewGuid(),
            cancellationToken: CancellationToken.None);

        Assert.IsFalse(
            result.IsError ?? false,
            "The remaining stub is a normal result, not a tool-level error: the transport worked " +
            "and the answer is 'not implemented'.");

        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

        StringAssert.Contains(text, "not implemented", "It must say it is not implemented.");
        StringAssert.Contains(
            text,
            "Missing prerequisite",
            "It must name the prerequisite that is missing, so the caller is not left guessing.");
        StringAssert.Contains(
            text,
            "queried no database",
            "get_search_results queries no database, and the caller must be told.");

        Assert.IsNull(
            result.StructuredContent,
            "It must not fabricate structured data: an empty or defaulted payload is " +
            "indistinguishable from a real result (ADR 044).");
    }

    [TestMethod]
    public void ToolDescriptions_WhenEmitted_MarkExactlyTheUnimplementedToolsAsSuch()
    {
        var options = BuildMcpServerOptions();

        // list_regions (task 005) and search_tours (task 006) are implemented, so neither
        // description warns that it is not — the marker would be a lie about live behaviour.
        foreach (var liveTool in new[] { "list_regions", "search_tours" })
        {
            StringAssert.DoesNotMatch(
                Description(options, liveTool),
                new Regex("NOT IMPLEMENTED"),
                $"'{liveTool}' is live: its description must no longer warn that it is not implemented.");
        }

        // get_search_results is the only stub left, and a caller reading only tools/list must be
        // warned (ADR 044).
        StringAssert.Contains(Description(options, "get_search_results"), "NOT IMPLEMENTED in this build");
    }

    private static McpServerOptions BuildMcpServerOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTourSearchApi(Configuration());

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [McpEndpointOptions.PathKey] = ContractMcpPath,
            [McpEndpointOptions.AdvertisedProtocolRevisionKey] = ContractProtocolRevision,
        })
        .Build();

    /// <summary>Resolves a registered tool, failing the test when the contract tool is absent.</summary>
    /// <param name="options">The MCP options of the built API graph.</param>
    /// <param name="toolName">The contract name of the tool.</param>
    /// <returns>The registered tool.</returns>
    /// <summary>Resolves the tool collection of a built API graph.</summary>
    /// <param name="options">The MCP options of the built API graph.</param>
    /// <returns>The registered tools, never <see langword="null"/>.</returns>
    private static McpServerPrimitiveCollection<McpServerTool> Tools(McpServerOptions options)
    {
        var tools = options.ToolCollection;

        Assert.IsNotNull(tools, "The MCP server registered no tool collection at all.");

        return tools!;
    }

    private static ModelContextProtocol.Protocol.Tool ContractTool(McpServerOptions options, string toolName)
    {
        var tool = Tools(options)[toolName];

        Assert.IsNotNull(tool, $"'{toolName}' is not registered (docs/mcp-tools.md).");

        return tool!.ProtocolTool;
    }

    private static JsonElement OutputSchema(McpServerOptions options, string toolName)
    {
        var schema = ContractTool(options, toolName).OutputSchema;

        Assert.IsTrue(schema.HasValue, $"'{toolName}' declares no outputSchema.");

        return schema.Value;
    }

    private static string Description(McpServerOptions options, string toolName) =>
        ContractTool(options, toolName).Description ?? string.Empty;

    private static List<string> PropertyNames(JsonElement schema) =>
        schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToList();

    private static List<string> RequiredNames(JsonElement schema) =>
        schema.TryGetProperty("required", out var required)
            ? required.EnumerateArray().Select(name => name.GetString()!).ToList()
            : [];

    private void SetVariable(string name, string? value)
    {
        _previousValues.TryAdd(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }
}
