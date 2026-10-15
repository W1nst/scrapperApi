using System.Text.RegularExpressions;
using TourSearch.Core.Configuration;
using TourSearch.Migrations;

namespace TourSearch.UnitTests.Regions;

/// <summary>
/// Guards the region catalog itself: the 85 Turkish regions, their slugs, their source ids, their
/// nesting, and the agreement between the two places the catalog is committed
/// (FR-001, FR-002, NFR-001).
/// </summary>
/// <remarks>
/// The assertions read the committed <c>selectors.json</c> and the migration's embedded seed — no
/// database and no network (NFR-003). The seed is what the migration inserts; the file is what a
/// human edits. A test asserting they agree is what turns "I forgot the migration" into a failing
/// build instead of a stale label served to an agent.
/// </remarks>
[TestClass]
public sealed class RegionCatalogSeedTests
{
    /// <summary>Child counts from the task's table, keyed by the parent's label.</summary>
    private static readonly Dictionary<string, int> ExpectedChildCounts = new(StringComparer.Ordinal)
    {
        ["Аланья"] = 9,
        ["Анталья"] = 6,
        ["Белек"] = 4,
        ["Бодрум"] = 6,
        ["Болу"] = 0,
        ["Бурса"] = 2,
        ["Даламан"] = 2,
        ["Дидим"] = 0,
        ["Измир"] = 0,
        ["Кайсери"] = 2,
        ["Каппадокия"] = 2,
        ["Кемер"] = 6,
        ["Кушадасы"] = 0,
        ["Мармарис"] = 4,
        ["Сарыкамыш"] = 0,
        ["Сиде"] = 9,
        ["Стамбул"] = 12,
        ["Фетхие"] = 1,
        ["Чешме"] = 0,
        ["Эрзурум"] = 0,
    };

    private static readonly SelectorsDocument Selectors =
        SelectorsDocument.Parse(RepositoryFiles.Read(SelectorsDocument.FileName));

    private static IReadOnlyList<RegionSeedRow> Seed => AddRegionCatalog.Seed;

    [TestMethod]
    public void Selectors_WhenParsed_DeclaresTheDocumentedSchemaTheSourceAndTheRegions()
    {
        Assert.AreEqual(
            SelectorsDocument.SupportedSchemaVersion,
            Selectors.SchemaVersion,
            "schema_version starts at 1 and this build understands exactly that (FR-003).");
        Assert.AreEqual(
            AddRegionCatalog.SourceName,
            Selectors.Source,
            "The file and the seed must name the same source: the catalog is one source's vocabulary.");
        Assert.HasCount(85, Selectors.Regions, "The file carries all 85 Turkish regions.");
        Assert.IsTrue(
            Selectors.UpdatedAt <= DateOnly.FromDateTime(DateTime.UtcNow),
            $"updated_at ({Selectors.UpdatedAt}) cannot be in the future.");
    }

    [TestMethod]
    public void Seed_WhenCounted_HoldsAllEightyFiveTurkishRegionsWithTheDocumentedSplit()
    {
        Assert.HasCount(85, Seed, "Turkey has 85 regions in the source's catalog: 20 parents + 65 children.");
        Assert.AreEqual(
            20,
            Seed.Count(region => region.ParentId is null),
            "20 top-level regions (FR-002).");
        Assert.AreEqual(
            65,
            Seed.Count(region => region.ParentId is not null),
            "65 child regions (FR-002).");
    }

    [TestMethod]
    public void Seed_WhenGroupedByParent_MatchesTheDocumentedChildCounts()
    {
        var parents = Seed.Where(region => region.ParentId is null).ToList();

        CollectionAssert.AreEquivalent(
            ExpectedChildCounts.Keys.ToList(),
            parents.Select(parent => parent.Label).ToList(),
            "The set of top-level regions changed: the source's catalog is not what the task recorded.");

        foreach (var parent in parents)
        {
            var children = Seed.Count(region => region.ParentId == parent.Id);

            Assert.AreEqual(
                ExpectedChildCounts[parent.Label],
                children,
                $"'{parent.Label}' must have the documented number of children (FR-002).");
        }
    }

    [TestMethod]
    public void Seed_WhenSlugsAreInspected_AreUniqueLowercaseHyphenatedAscii()
    {
        var slugs = Seed.Select(region => region.Id).ToList();

        Assert.HasCount(
            slugs.Count,
            slugs.Distinct(StringComparer.Ordinal).ToList(),
            $"Slugs must be unique: {string.Join(", ", slugs.GroupBy(slug => slug).Where(g => g.Count() > 1).Select(g => g.Key))}");

        foreach (var slug in slugs)
        {
            Assert.IsTrue(
                Regex.IsMatch(slug, "^[a-z0-9]+(?:-[a-z0-9]+)*$"),
                $"'{slug}' is not a lowercase ASCII slug with hyphens as separators (FR-002).");
        }
    }

    [TestMethod]
    public void Seed_WhenSourceIdsAreInspected_AreNumericUniqueAndWithinTheColumnWidth()
    {
        var sourceIds = Seed.Select(region => region.SourceId).ToList();

        Assert.HasCount(
            sourceIds.Count,
            sourceIds.Distinct(StringComparer.Ordinal).ToList(),
            "Two regions cannot claim the same id of one source (the unique index enforces the same rule).");

        foreach (var (id, sourceId) in Seed.Select(region => (region.Id, region.SourceId)))
        {
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(sourceId),
                $"'{id}' carries no source id, so a parser could not build the source's request for it.");
            Assert.IsTrue(
                sourceId.All(char.IsAsciiDigit),
                $"'{id}' has source id '{sourceId}', which is not numeric (FR-001).");
            Assert.IsLessThanOrEqualTo(
                TourSearch.Core.Entities.Region.SourceIdMaxLength,
                sourceId.Length,
                $"'{id}' has a source id longer than the column can hold.");
        }
    }

    [TestMethod]
    public void Seed_WhenNestingIsInspected_EveryParentExistsAndIsNotTheRegionItself()
    {
        var ids = Seed.Select(region => region.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var region in Seed.Where(region => region.ParentId is not null))
        {
            Assert.Contains(
                region.ParentId!,
                ids,
                $"'{region.Id}' points at parent '{region.ParentId}', which no region defines: the " +
                "foreign key would reject it at insert time (FR-001).");
            Assert.AreNotEqual(
                region.Id,
                region.ParentId,
                $"'{region.Id}' is its own parent.");
        }
    }

    [TestMethod]
    [DataRow("kemer", "22", DisplayName = "kemer -> 22")]
    [DataRow("alanya", "19", DisplayName = "alanya -> 19")]
    [DataRow("istanbul", "277", DisplayName = "istanbul -> 277")]
    [DataRow("side", "23", DisplayName = "side -> 23")]
    [DataRow("belek", "21", DisplayName = "belek -> 21")]
    public void Seed_WhenSpotChecked_MapsEachSlugToTheSourceIdOfTheCollectedData(string slug, string sourceId)
    {
        var region = Seed.SingleOrDefault(candidate => candidate.Id == slug);

        Assert.IsNotNull(region, $"'{slug}' is missing from the catalog.");
        Assert.AreEqual(sourceId, region.SourceId, $"'{slug}' must round-trip to the source's own id.");
    }

    [TestMethod]
    public void Seed_WhenLabelsAreInspected_KeepsTheSourceWordingVerbatim()
    {
        Assert.AreEqual(
            "Кемер",
            Seed.Single(region => region.Id == "kemer").Label,
            "Labels are the source's own Cyrillic names, not translated.");

        // The source writes "Бодрум - центр" with spaces around the hyphen; the slug rule turns
        // spaces and " - " into a single separator.
        Assert.AreEqual(
            "Бодрум - центр",
            Seed.Single(region => region.Id == "bodrum-tsentr").Label,
            "Nested names stay verbatim, including the source's spacing.");
    }

    [TestMethod]
    public void Seed_WhenRendered_IsAnIdempotentUpsertInParentFirstOrder()
    {
        var sql = AddRegionCatalog.BuildSeedStatement();

        StringAssert.StartsWith(
            sql,
            "INSERT INTO tour_search.regions (id, label, source, active, source_id, parent_id)");
        StringAssert.Contains(sql, "ON CONFLICT (id) DO UPDATE SET", "Re-applying corrects instead of failing (FR-004).");
        StringAssert.Contains(sql, "label = EXCLUDED.label");
        StringAssert.Contains(sql, "source_id = EXCLUDED.source_id");
        StringAssert.Contains(sql, "parent_id = EXCLUDED.parent_id");
        Assert.DoesNotContain(
            "active = EXCLUDED.active",
            sql,
            "Re-applying the seed must not resurrect a region a human deactivated.");

        Assert.AreEqual(
            Seed.Count,
            sql.Split("\n").Count(line => line.TrimStart().StartsWith("('", StringComparison.Ordinal)),
            "Every region is one VALUES row; a missing row would silently shrink the catalog.");

        // Parents come first, so the self-referencing foreign key is satisfied as rows are inserted.
        foreach (var child in Seed.Where(region => region.ParentId is not null))
        {
            Assert.IsLessThan(
                IndexOf(sql, child.Id),
                IndexOf(sql, child.ParentId!),
                $"'{child.ParentId}' must be inserted before its child '{child.Id}'.");
        }
    }

    [TestMethod]
    public void Seed_WhenUnseeded_RemovesChildrenBeforeTheirParents()
    {
        var statements = AddRegionCatalog.BuildUnseedStatements().ToList();

        Assert.HasCount(2, statements, "One statement for the children, one for the parents.");
        StringAssert.Contains(statements[0], "'tekirova'", "The children go first: parent_id is RESTRICT.");
        Assert.DoesNotContain("'kemer'", statements[0], "The parents are not in the children's statement.");
        StringAssert.Contains(statements[1], "'kemer'");
    }

    private static int IndexOf(string sql, string slug)
    {
        var index = sql.IndexOf($"('{slug}',", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, index, $"'{slug}' is not among the seeded VALUES rows.");

        return index;
    }

    [TestMethod]
    public void Seed_WhenComparedWithSelectorsJson_AgreesSoTheFileCannotDriftFromTheMigration()
    {
        var fromSeed = Seed
            .Select(region => (region.Id, region.SourceId, region.Label, region.ParentId))
            .OrderBy(row => row.Id, StringComparer.Ordinal)
            .ToList();

        var fromFile = Selectors.Regions
            .Select(region => (region.Id, region.SourceId, region.Label, region.ParentId))
            .OrderBy(row => row.Id, StringComparer.Ordinal)
            .ToList();

        // NFR-001: editing selectors.json without adding a migration fails here instead of serving
        // stale names or ids to an agent.
        CollectionAssert.AreEqual(
            fromSeed,
            fromFile,
            "selectors.json and the seeded catalog disagree. Add a migration that updates the " +
            "regions it changed — the file is what a human edits, and the table is what the tools read " +
            "(NFR-001, ADR 046).");
    }
}
