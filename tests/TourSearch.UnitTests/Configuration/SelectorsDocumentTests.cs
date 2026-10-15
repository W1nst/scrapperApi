using TourSearch.Core.Configuration;

namespace TourSearch.UnitTests.Configuration;

/// <summary>
/// Guards the validation of <c>selectors.json</c> (FR-003): the committed file must satisfy the
/// document's minimal schema, and a malformed file must fail with a message naming the offending
/// key instead of failing later, at the first job.
/// </summary>
/// <remarks>
/// Nothing here contacts the network or a database: the document is a string, and the committed file
/// is read from the working tree.
/// </remarks>
[TestClass]
public sealed class SelectorsDocumentTests
{
    private const string ValidDocument = """
        {
          "schema_version": 1,
          "source": "tourvisor",
          "updated_at": "2026-10-15",
          "regions": [
            { "id": "kemer", "source_id": "22", "label": "Кемер" },
            { "id": "kemer-tekirova", "source_id": "2223", "label": "Текирова", "parent_id": "kemer" }
          ]
        }
        """;

    [TestMethod]
    public void Parse_WhenTheCommittedFileIsGiven_ReturnsTheDocumentedDocument()
    {
        var document = SelectorsDocument.Parse(RepositoryFiles.Read(SelectorsDocument.FileName));

        Assert.AreEqual(SelectorsDocument.SupportedSchemaVersion, document.SchemaVersion);
        Assert.AreEqual("tourvisor", document.Source);
        Assert.HasCount(85, document.Regions, "The committed file carries the whole Turkish catalog.");

        var parent = document.Regions.Single(region => region.Id == "kemer");
        Assert.IsNull(parent.ParentId, "A top-level region has no parent.");

        var child = document.Regions.Single(region => region.Id == "tekirova");
        Assert.AreEqual("kemer", child.ParentId, "A nested region names its parent by slug.");
    }

    [TestMethod]
    public void Parse_WhenTheDocumentIsValid_AcceptsAnUnknownTopLevelSection()
    {
        var document = SelectorsDocument.Parse(
            ValidDocument.Replace(
                "\"regions\"",
                "\"pages\": { \"search_results\": {} },\n  \"regions\"",
                StringComparison.Ordinal));

        Assert.HasCount(2, document.Regions, "Step 9 adds pages/fields/fallback_selectors to this file.");
    }

    [TestMethod]
    [DataRow("{ not json", "not valid JSON", DisplayName = "not JSON at all")]
    [DataRow("[]", "root must be an object", DisplayName = "root is an array")]
    [DataRow("{}", "schema_version is required", DisplayName = "no schema_version")]
    [DataRow("{\"schema_version\": \"1\"}", "schema_version must be an integer", DisplayName = "schema_version as a string")]
    [DataRow("{\"schema_version\": 2}", "schema_version 2 is not supported", DisplayName = "unknown schema_version")]
    [DataRow("{\"schema_version\": 1}", "source is required", DisplayName = "no source")]
    [DataRow("{\"schema_version\": 1, \"source\": \"\"}", "source must not be empty", DisplayName = "empty source")]
    [DataRow("{\"schema_version\": 1, \"source\": \"tourvisor\"}", "updated_at is required", DisplayName = "no updated_at")]
    [DataRow("{\"schema_version\": 1, \"source\": \"tourvisor\", \"updated_at\": \"15.10.2026\"}", "yyyy-MM-dd", DisplayName = "updated_at is not a date")]
    [DataRow("{\"schema_version\": 1, \"source\": \"tourvisor\", \"updated_at\": \"2026-10-15\"}", "regions is required", DisplayName = "no regions")]
    [DataRow("{\"schema_version\": 1, \"source\": \"tourvisor\", \"updated_at\": \"2026-10-15\", \"regions\": {}}", "regions must be an array", DisplayName = "regions is not an array")]
    [DataRow("{\"schema_version\": 1, \"source\": \"tourvisor\", \"updated_at\": \"2026-10-15\", \"regions\": []}", "regions must not be empty", DisplayName = "empty regions")]
    public void Parse_WhenTheDocumentIsMalformed_FailsNamingTheOffendingKey(string json, string expectedFragment)
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => SelectorsDocument.Parse(json));

        StringAssert.Contains(exception.Message, expectedFragment);
        StringAssert.Contains(
            exception.Message,
            SelectorsDocument.FileName,
            "The failure must name the file, so a startup error points at the file to edit.");
    }

    [TestMethod]
    [DataRow(
        "{\"schema_version\": 1, \"source\": \"tourvisor\", \"updated_at\": \"2026-10-15\", \"regions\": [\"kemer\"]}",
        "regions[0] must be an object",
        DisplayName = "a region is not an object")]
    [DataRow(
        "{\"schema_version\": 1, \"source\": \"tourvisor\", \"updated_at\": \"2026-10-15\", \"regions\": [{\"source_id\": \"22\", \"label\": \"Кемер\"}]}",
        "regions[0].id is required",
        DisplayName = "a region has no id")]
    [DataRow(
        "{\"schema_version\": 1, \"source\": \"tourvisor\", \"updated_at\": \"2026-10-15\", \"regions\": [{\"id\": \"Кемер\", \"source_id\": \"22\", \"label\": \"Кемер\"}]}",
        "regions[0].id",
        DisplayName = "the id is not a slug")]
    [DataRow(
        "{\"schema_version\": 1, \"source\": \"tourvisor\", \"updated_at\": \"2026-10-15\", \"regions\": [{\"id\": \"kemer\", \"source_id\": 22, \"label\": \"Кемер\"}]}",
        "regions[0].source_id must be a string",
        DisplayName = "the source id is a JSON number")]
    [DataRow(
        "{\"schema_version\": 1, \"source\": \"tourvisor\", \"updated_at\": \"2026-10-15\", \"regions\": [{\"id\": \"kemer\", \"source_id\": \"22a\", \"label\": \"Кемер\"}]}",
        "regions[0].source_id must be a numeric string",
        DisplayName = "the source id is not numeric")]
    [DataRow(
        "{\"schema_version\": 1, \"source\": \"tourvisor\", \"updated_at\": \"2026-10-15\", \"regions\": [{\"id\": \"kemer\", \"source_id\": \"22\"}]}",
        "regions[0].label is required",
        DisplayName = "a region has no label")]
    [DataRow(
        "{\"schema_version\": 1, \"source\": \"tourvisor\", \"updated_at\": \"2026-10-15\", \"regions\": [{\"id\": \"kemer\", \"source_id\": \"22\", \"label\": \"Кемер\", \"parent_id\": 1}]}",
        "regions[0].parent_id must be a region slug",
        DisplayName = "the parent id is not a slug")]
    [DataRow(
        "{\"schema_version\": 1, \"source\": \"tourvisor\", \"updated_at\": \"2026-10-15\", \"regions\": [{\"id\": \"kemer\", \"source_id\": \"22\", \"label\": \"Кемер\", \"parent_id\": \"atlantis\"}]}",
        "names a region this document does not define",
        DisplayName = "the parent does not exist")]
    [DataRow(
        "{\"schema_version\": 1, \"source\": \"tourvisor\", \"updated_at\": \"2026-10-15\", \"regions\": [{\"id\": \"kemer\", \"source_id\": \"22\", \"label\": \"Кемер\", \"parent_id\": \"kemer\"}]}",
        "is the region itself",
        DisplayName = "a region is its own parent")]
    [DataRow(
        "{\"schema_version\": 1, \"source\": \"tourvisor\", \"updated_at\": \"2026-10-15\", \"regions\": [{\"id\": \"kemer\", \"source_id\": \"22\", \"label\": \"Кемер\", \"sourc_id\": \"19\"}]}",
        "unknown property",
        DisplayName = "a region entry has a typo'd key")]
    [DataRow(
        "{\"schema_version\": 1, \"source\": \"tourvisor\", \"updated_at\": \"2026-10-15\", \"regions\": [{\"id\": \"kemer\", \"source_id\": \"22\", \"label\": \"Кемер\"}, {\"id\": \"kemer\", \"source_id\": \"19\", \"label\": \"Кемер 2\"}]}",
        "is declared more than once",
        DisplayName = "the region id is duplicated")]
    public void Parse_WhenARegionEntryIsMalformed_FailsNamingTheEntryAndIndex(string json, string expectedFragment)
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => SelectorsDocument.Parse(json));

        StringAssert.Contains(exception.Message, "regions[");
        StringAssert.Contains(exception.Message, expectedFragment);
    }

    [TestMethod]
    public void Parse_WhenTwoRegionsShareASourceId_FailsBecauseTheSourceIdIdentifiesTheRegion()
    {
        const string json = """
            {
              "schema_version": 1,
              "source": "tourvisor",
              "updated_at": "2026-10-15",
              "regions": [
                { "id": "kemer", "source_id": "22", "label": "Кемер" },
                { "id": "alanya", "source_id": "22", "label": "Аланья" }
              ]
            }
            """;

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => SelectorsDocument.Parse(json));

        StringAssert.Contains(exception.Message, "regions[1].source_id");
        StringAssert.Contains(exception.Message, "declared more than once");
    }
}
