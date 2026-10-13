using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TourSearch.Core.Entities;
using TourSearch.Core.Enums;
using TourSearch.Infrastructure;

namespace TourSearch.UnitTests.Infrastructure;

/// <summary>
/// Guards the persistence shape of the single <see cref="TourSearchDbContext"/> against
/// docs/database.md: tables, columns, column types, indexes and delete behaviours.
/// </summary>
/// <remarks>
/// No database is contacted (docs/database.md, FR/NFR-003): every assertion reads
/// <c>context.Model</c> or the script produced by <c>Database.GenerateCreateScript()</c>,
/// which are computed from the model alone. Generating the script does not open a
/// connection, and the placeholder connection string below carries no credentials.
/// </remarks>
[TestClass]
public sealed class ModelShapeTests
{
    private const string PlaceholderConnectionString = "Host=localhost;Database=toursearch";

    [TestMethod]
    public void CreateScript_WhenGenerated_DeclaresExactlyTheThreeDocumentedTables()
    {
        var script = CreateScript();

        var tableNames = Regex
            .Matches(script, @"^CREATE TABLE (?<name>\S+) \($", RegexOptions.Multiline)
            .Select(m => m.Groups["name"].Value)
            .ToList();

        CollectionAssert.AreEquivalent(
            new[] { "tour_search.regions", "tour_search.search_jobs", "tour_search.tour_offers" },
            tableNames,
            $"Unexpected table set — SearchParameters must not become a table. Script:\n{script}");
    }

    [TestMethod]
    public void CreateScript_WhenGenerated_MatchesTheDocumentedColumnsOfEveryTable()
    {
        var script = CreateScript();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "id", "parameters_hash", "currency_code", "status", "attempt", "lease_token",
                "lease_expires_at", "cancel_requested_at", "created_at", "started_at", "completed_at",
                "degraded", "warnings", "offers_count", "pages_done", "pages_total", "error_code",
                "error_message", "retryable", "parameters",
            },
            ColumnNames(TableBody(script, "tour_search.search_jobs")),
            "search_jobs columns differ from docs/domain.md (Offers must not become a column).");

        CollectionAssert.AreEquivalent(
            new[]
            {
                "id", "search_job_id", "hotel_name", "hotel_stars", "luxury", "region_id", "check_in",
                "nights", "price", "currency_code", "room_type", "meal_type", "source_url", "scraped_at",
            },
            ColumnNames(TableBody(script, "tour_search.tour_offers")),
            "tour_offers columns differ from docs/domain.md (Raw is [PLANNED] and must be absent).");

        CollectionAssert.AreEquivalent(
            new[] { "id", "label", "source", "active" },
            ColumnNames(TableBody(script, "tour_search.regions")),
            "regions columns differ from docs/domain.md.");
    }

    [TestMethod]
    public void CreateScript_WhenGenerated_MapsParametersToJsonbAndWarningsToTextArray()
    {
        var script = CreateScript();

        AssertScriptContains(script, "parameters jsonb NOT NULL");
        AssertScriptContains(script, "warnings text[] NOT NULL");

        // Currency is a value, not a fixed-width field: varchar(3), never char(3).
        AssertScriptContains(script, "currency_code character varying(3)");
        Assert.IsFalse(
            script.Contains("character(3)", StringComparison.Ordinal),
            "currency_code must be varchar(3), not char(3).");

        // Enums as their member names.
        AssertScriptContains(script, "status character varying(20) NOT NULL");
        AssertScriptContains(script, "error_code character varying(40)");

        // Persisted timestamps are timestamptz (docs/database.md#utc-trap).
        AssertScriptContains(script, "created_at timestamp with time zone NOT NULL");
        AssertScriptContains(script, "scraped_at timestamp with time zone NOT NULL");

        // Price band width from docs/domain.md: decimal(12,2).
        AssertScriptContains(script, "price numeric(12,2) NOT NULL");
    }

    [TestMethod]
    public void CreateScript_WhenGenerated_DeclaresTheDocumentedIndexesInColumnOrder()
    {
        var script = CreateScript();

        AssertIndex(script, "ix_search_jobs_status_created_at", "tour_search.search_jobs", "status, created_at");
        AssertIndex(script, "ix_search_jobs_status_lease_expires_at", "tour_search.search_jobs", "status, lease_expires_at");
        AssertIndex(script, "ix_search_jobs_parameters_hash", "tour_search.search_jobs", "parameters_hash");
        AssertIndex(script, "ix_tour_offers_search_job_id", "tour_search.tour_offers", "search_job_id");
        AssertIndex(script, "ix_tour_offers_region_id", "tour_search.tour_offers", "region_id");
        AssertIndex(script, "ix_tour_offers_scraped_at_id", "tour_search.tour_offers", "scraped_at, id");

        // Created now while the table is empty; droppable in a later migration if the agent
        // never sorts/filters offers by them.
        AssertIndex(script, "ix_tour_offers_price", "tour_search.tour_offers", "price");
        AssertIndex(script, "ix_tour_offers_check_in", "tour_search.tour_offers", "check_in");

        using var context = CreateContext();
        var jobType = FindEntityType<SearchJob>(context);

        CollectionAssert.AreEqual(
            new[] { nameof(SearchJob.Status), nameof(SearchJob.CreatedAt) },
            IndexProperties(jobType, "ix_search_jobs_status_created_at"),
            "The job-pickup index must be (status, created_at), equality column first.");

        CollectionAssert.AreEqual(
            new[] { nameof(SearchJob.Status), nameof(SearchJob.LeaseExpiresAt) },
            IndexProperties(jobType, "ix_search_jobs_status_lease_expires_at"),
            "The lease-recovery index must be (status, lease_expires_at), equality column first.");

        CollectionAssert.AreEqual(
            new[] { nameof(TourOffer.ScrapedAt), nameof(TourOffer.Id) },
            IndexProperties(FindEntityType<TourOffer>(context), "ix_tour_offers_scraped_at_id"),
            "The offer pagination cursor must be (scraped_at, id).");
    }

    [TestMethod]
    public void ForeignKeys_WhenConfigured_UseTheDocumentedDeleteBehaviours()
    {
        using var context = CreateContext();
        var offerType = FindEntityType<TourOffer>(context);

        var jobForeignKey = offerType
            .GetForeignKeys()
            .Single(fk => fk.Properties.Single().Name == nameof(TourOffer.SearchJobId));
        Assert.AreEqual(typeof(SearchJob), jobForeignKey.PrincipalEntityType.ClrType);
        Assert.AreEqual(
            DeleteBehavior.Cascade,
            jobForeignKey.DeleteBehavior,
            "Offers follow their job (docs/database.md#cleanup-ttl).");

        var regionForeignKey = offerType
            .GetForeignKeys()
            .Single(fk => fk.Properties.Single().Name == nameof(TourOffer.RegionId));
        Assert.AreEqual(typeof(Region), regionForeignKey.PrincipalEntityType.ClrType);
        Assert.AreEqual(
            DeleteBehavior.Restrict,
            regionForeignKey.DeleteBehavior,
            "A region is deactivated, never deleted — Cascade is EF's default here and would " +
            "delete every offer of that region (docs/domain.md#region-catalog).");

        var script = CreateScript();
        AssertScriptContains(script, "ON DELETE CASCADE");
        AssertScriptContains(script, "ON DELETE RESTRICT");
    }

    [TestMethod]
    public void Model_WhenBuilt_ContainsExactlyOneDbContextType()
    {
        var tourSearchAssemblies = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("TourSearch.", StringComparison.Ordinal) == true)
            .ToList();

        Assert.Contains(
            typeof(TourSearchDbContext).Assembly,
            tourSearchAssemblies,
            "TourSearch.Infrastructure was not loaded, so this guard would prove nothing.");

        var contextTypes = tourSearchAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && typeof(DbContext).IsAssignableFrom(t))
            .ToList();

        Assert.HasCount(
            1,
            contextTypes,
            $"One DbContext, in Infrastructure (docs/architecture.md): {string.Join(", ", contextTypes.Select(t => t.FullName))}");
        Assert.AreEqual(typeof(TourSearchDbContext), contextTypes[0]);
    }

    [TestMethod]
    public void EnumProperties_WhenConverted_StoreTheMemberNameVerbatim()
    {
        using var context = CreateContext();
        var jobType = FindEntityType<SearchJob>(context);

        var status = jobType.FindProperty(nameof(SearchJob.Status));
        Assert.IsNotNull(status);
        Assert.AreEqual("character varying(20)", status.GetColumnType());

        var statusConverter = status.GetTypeMapping().Converter;
        Assert.IsNotNull(statusConverter, "JobStatus must be converted to a string, not stored as an integer.");
        Assert.AreEqual(typeof(string), statusConverter.ProviderClrType);

        foreach (var value in Enum.GetValues<JobStatus>())
        {
            Assert.AreEqual(value.ToString(), statusConverter.ConvertToProvider(value));
            Assert.AreEqual(value, statusConverter.ConvertFromProvider(value.ToString()));
        }

        var errorCode = jobType.FindProperty(nameof(SearchJob.ErrorCode));
        Assert.IsNotNull(errorCode);
        Assert.AreEqual("character varying(40)", errorCode.GetColumnType());

        var errorCodeConverter = errorCode.GetTypeMapping().Converter;
        Assert.IsNotNull(errorCodeConverter, "JobErrorCode must be converted to a string, not stored as an integer.");
        Assert.AreEqual(typeof(string), errorCodeConverter.ProviderClrType);

        foreach (var value in Enum.GetValues<JobErrorCode>())
        {
            Assert.AreEqual(value.ToString(), errorCodeConverter.ConvertToProvider(value));
            Assert.AreEqual(value, errorCodeConverter.ConvertFromProvider(value.ToString()));
        }
    }

    [TestMethod]
    public void Model_WhenBuilt_MapsParametersAsAComplexTypeAndOffersAsANavigationOnly()
    {
        using var context = CreateContext();
        var jobType = FindEntityType<SearchJob>(context);

        Assert.IsEmpty(
            context.Model.GetEntityTypes().Where(et => et.ClrType == typeof(SearchParameters)),
            "SearchParameters must be a complex type, never an entity of its own.");

        var parameters = jobType.FindComplexProperty(nameof(SearchJob.Parameters));
        Assert.IsNotNull(parameters, "SearchJob.Parameters must be a complex property.");
        Assert.AreEqual("parameters", parameters.ComplexType.GetContainerColumnName());

        Assert.IsNull(jobType.FindProperty(nameof(SearchJob.Offers)), "Offers must not be a column.");
        Assert.IsNotNull(jobType.FindNavigation(nameof(SearchJob.Offers)), "Offers must still be a navigation.");

        var warnings = jobType.FindProperty(nameof(SearchJob.Warnings));
        Assert.IsNotNull(warnings);
        Assert.AreEqual("text[]", warnings.GetColumnType(), "Warnings must be a native PostgreSQL text[] array.");
        var element = warnings.GetElementType();
        Assert.IsNotNull(element);
        Assert.AreEqual(typeof(string), element.ClrType);
    }

    [TestMethod]
    public void Model_WhenBuilt_UsesTheDocumentedStringWidthsAndLeavesLabelsUnbounded()
    {
        using var context = CreateContext();
        var jobType = FindEntityType<SearchJob>(context);
        var offerType = FindEntityType<TourOffer>(context);
        var regionType = FindEntityType<Region>(context);

        Assert.AreEqual(71, jobType.FindProperty(nameof(SearchJob.ParametersHash))?.GetMaxLength());
        Assert.AreEqual(20, jobType.FindProperty(nameof(SearchJob.Status))?.GetMaxLength());
        Assert.AreEqual(40, jobType.FindProperty(nameof(SearchJob.ErrorCode))?.GetMaxLength());
        Assert.AreEqual(3, jobType.FindProperty(nameof(SearchJob.CurrencyCode))?.GetMaxLength());
        Assert.AreEqual(3, offerType.FindProperty(nameof(TourOffer.CurrencyCode))?.GetMaxLength());

        // Unbounded text by decision: region labels, and free-text source values.
        Assert.IsNull(regionType.FindProperty(nameof(Region.Label))?.GetMaxLength());
        Assert.IsNull(regionType.FindProperty(nameof(Region.Id))?.GetMaxLength());
        Assert.IsNull(offerType.FindProperty(nameof(TourOffer.HotelName))?.GetMaxLength());
    }

    private static TourSearchDbContext CreateContext() => new(
        new DbContextOptionsBuilder<TourSearchDbContext>()
            .UseNpgsql(PlaceholderConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    private static string CreateScript()
    {
        using var context = CreateContext();
        return context.Database.GenerateCreateScript().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static IEntityType FindEntityType<TEntity>(DbContext context)
    {
        var entityType = context.Model.FindEntityType(typeof(TEntity));
        Assert.IsNotNull(entityType, $"{typeof(TEntity).Name} is not part of the model.");
        return entityType;
    }

    private static List<string> IndexProperties(IEntityType entityType, string databaseName)
    {
        var index = entityType.GetIndexes().SingleOrDefault(i => i.GetDatabaseName() == databaseName);
        Assert.IsNotNull(index, $"No index named {databaseName} on {entityType.DisplayName()}.");
        return index.Properties.Select(p => p.Name).ToList();
    }

    private static string TableBody(string script, string tableName)
    {
        var match = Regex.Match(
            script,
            $@"CREATE TABLE {Regex.Escape(tableName)} \((?<body>.*?)\);",
            RegexOptions.Singleline);
        Assert.IsTrue(match.Success, $"No CREATE TABLE statement for {tableName}. Script:\n{script}");
        return match.Groups["body"].Value;
    }

    private static List<string> ColumnNames(string tableBody) => tableBody
        .Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith("CONSTRAINT", StringComparison.Ordinal))
        .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
        .ToList();

    private static void AssertScriptContains(string script, string expected) => Assert.IsTrue(
        script.Contains(expected, StringComparison.Ordinal),
        $"The generated script does not contain \"{expected}\". Script:\n{script}");

    private static void AssertIndex(string script, string indexName, string tableName, string columns)
    {
        var pattern =
            $@"^CREATE INDEX {Regex.Escape(indexName)} ON {Regex.Escape(tableName)} \({Regex.Escape(columns)}\);$";
        Assert.IsTrue(
            Regex.IsMatch(script, pattern, RegexOptions.Multiline),
            $"No index {indexName} ON {tableName} ({columns}). Script:\n{script}");
    }
}
