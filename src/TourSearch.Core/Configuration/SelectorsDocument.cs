using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TourSearch.Core.Entities;

namespace TourSearch.Core.Configuration;

/// <summary>
/// The validated contents of <c>selectors.json</c>: which source this build scrapes, the region
/// catalog collected from it, and — in a later step — the selectors themselves
/// (docs/scraping.md#selectors).
/// </summary>
/// <remarks>
/// <para>
/// The file is validated against the minimal schema below, and a malformed file must fail startup
/// rather than the first job (docs/scraping.md#selectors). The schema is expressed in code, with
/// the same keywords a JSON Schema document would carry — <c>type</c>, <c>required</c>,
/// <c>pattern</c> and <c>additionalProperties: false</c> for a region entry — so the failure names
/// the offending key and index instead of reporting a generic parse error.
/// </para>
/// <para>
/// This type does <b>no I/O</b>: it parses a document it is given. Reading the file is the
/// startup's job, and the catalog itself is seeded into the database by migration, not read from
/// this file at runtime (ADR 046).
/// </para>
/// <para>
/// Unknown <b>top-level</b> properties are accepted on purpose: step 9 adds <c>pages</c>,
/// <c>fields</c> and <c>fallback_selectors</c> to this same file. An unknown property <b>inside a
/// region entry</b> is rejected, because a typo there would silently drop a region's field.
/// </para>
/// </remarks>
/// <param name="SchemaVersion">The document's schema version; must be <see cref="SupportedSchemaVersion"/>.</param>
/// <param name="Source">The source the catalog and the selectors belong to, e.g. <c>"tourvisor"</c>.</param>
/// <param name="UpdatedAt">The date the file was last edited, day precision.</param>
/// <param name="Regions">The region catalog, in file order.</param>
public sealed partial record SelectorsDocument(
    int SchemaVersion,
    string Source,
    DateOnly UpdatedAt,
    IReadOnlyList<SelectorsRegion> Regions)
{
    /// <summary>The file's name at the repository root and next to the deployed binaries.</summary>
    public const string FileName = "selectors.json";

    /// <summary>The only <c>schema_version</c> this build understands.</summary>
    public const int SupportedSchemaVersion = 1;

    private static readonly string[] RegionPropertyNames = ["id", "source_id", "label", "parent_id"];

    /// <summary>Parses and validates a <c>selectors.json</c> document.</summary>
    /// <param name="json">The document's text.</param>
    /// <returns>The validated document.</returns>
    /// <exception cref="InvalidOperationException">
    /// The document does not satisfy the schema: it is not valid JSON, a required key is missing or
    /// has the wrong type, a slug or a source id is malformed, a region id or source id is
    /// duplicated, a <c>parent_id</c> names no region of this document, or the schema version is not
    /// supported. The message names the offending key and index.
    /// </exception>
    public static SelectorsDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw Invalid("the document is empty");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw Invalid($"it is not valid JSON: {exception.Message}");
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid($"the document root must be an object, not {Describe(root)}");
            }

            var schemaVersion = ReadSchemaVersion(root);
            var source = RequiredString(root, "source", "source");
            var updatedAt = ReadUpdatedAt(root);
            var regions = ReadRegions(root);

            return new SelectorsDocument(schemaVersion, source, updatedAt, regions);
        }
    }

    private static int ReadSchemaVersion(JsonElement root)
    {
        if (!root.TryGetProperty("schema_version", out var element))
        {
            throw Invalid("schema_version is required and is missing");
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var version))
        {
            throw Invalid($"schema_version must be an integer, not {Describe(element)}");
        }

        if (version != SupportedSchemaVersion)
        {
            throw Invalid(
                $"schema_version {version} is not supported by this build " +
                $"(supported: {SupportedSchemaVersion})");
        }

        return version;
    }

    private static DateOnly ReadUpdatedAt(JsonElement root)
    {
        var text = RequiredString(root, "updated_at", "updated_at");

        if (!DateOnly.TryParseExact(
                text,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var updatedAt))
        {
            throw Invalid(
                $"updated_at must be a date-only string in yyyy-MM-dd form, not {Quote(text)}");
        }

        return updatedAt;
    }

    private static IReadOnlyList<SelectorsRegion> ReadRegions(JsonElement root)
    {
        if (!root.TryGetProperty("regions", out var element))
        {
            throw Invalid("regions is required and is missing");
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"regions must be an array, not {Describe(element)}");
        }

        var entries = element.EnumerateArray().ToList();

        if (entries.Count == 0)
        {
            throw Invalid(
                "regions must not be empty: an empty catalog makes every search succeed with zero " +
                "offers, which is indistinguishable from 'no such region'");
        }

        var regions = new List<SelectorsRegion>(entries.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < entries.Count; index++)
        {
            regions.Add(ReadRegion(entries[index], index, ids, sourceIds));
        }

        // The nesting is checked once every id is known: a parent may be declared after its child.
        for (var index = 0; index < regions.Count; index++)
        {
            var region = regions[index];

            if (region.ParentId is { } parentId && !ids.Contains(parentId))
            {
                throw Invalid(
                    $"regions[{index}].parent_id {Quote(parentId)} names a region this document does " +
                    "not define");
            }
        }

        return regions;
    }

    private static SelectorsRegion ReadRegion(
        JsonElement element,
        int index,
        HashSet<string> ids,
        HashSet<string> sourceIds)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"regions[{index}] must be an object, not {Describe(element)}");
        }

        RejectUnknownRegionProperties(element, index);

        var id = RequiredString(element, "id", $"regions[{index}].id");

        if (!Slug().IsMatch(id))
        {
            throw Invalid(
                $"regions[{index}].id {Quote(id)} must be a lowercase ASCII slug, hyphen separated " +
                "(for example \"kemer\")");
        }

        if (!ids.Add(id))
        {
            throw Invalid($"regions[{index}].id {Quote(id)} is declared more than once");
        }

        var sourceId = RequiredString(element, "source_id", $"regions[{index}].source_id");

        if (sourceId.Length > Region.SourceIdMaxLength || !Numeric().IsMatch(sourceId))
        {
            throw Invalid(
                $"regions[{index}].source_id must be a numeric string of at most " +
                $"{Region.SourceIdMaxLength} digits, not {Quote(sourceId)}");
        }

        if (!sourceIds.Add(sourceId))
        {
            throw Invalid(
                $"regions[{index}].source_id {Quote(sourceId)} is declared more than once: two regions " +
                "cannot claim the same id of one source");
        }

        var label = RequiredString(element, "label", $"regions[{index}].label");

        return new SelectorsRegion(id, sourceId, label, ReadParentId(element, index, id));
    }

    private static string? ReadParentId(JsonElement element, int index, string id)
    {
        if (!element.TryGetProperty("parent_id", out var parent) || parent.ValueKind == JsonValueKind.Null)
        {
            // Absent or null both mean "top-level region".
            return null;
        }

        if (parent.ValueKind != JsonValueKind.String)
        {
            throw Invalid(
                $"regions[{index}].parent_id must be a region slug, not {Describe(parent)}");
        }

        var parentId = parent.GetString()!;

        if (!Slug().IsMatch(parentId))
        {
            throw Invalid(
                $"regions[{index}].parent_id {Quote(parentId)} must be a lowercase ASCII slug");
        }

        if (string.Equals(parentId, id, StringComparison.Ordinal))
        {
            throw Invalid($"regions[{index}].parent_id {Quote(parentId)} is the region itself");
        }

        return parentId;
    }

    private static void RejectUnknownRegionProperties(JsonElement element, int index)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!RegionPropertyNames.Contains(property.Name, StringComparer.Ordinal))
            {
                throw Invalid(
                    $"regions[{index}] has an unknown property {Quote(property.Name)}; allowed: " +
                    string.Join(", ", RegionPropertyNames));
            }
        }
    }

    private static string RequiredString(JsonElement owner, string propertyName, string path)
    {
        if (!owner.TryGetProperty(propertyName, out var element))
        {
            throw Invalid($"{path} is required and is missing");
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"{path} must be a string, not {Describe(element)}");
        }

        var value = element.GetString()!;

        if (string.IsNullOrWhiteSpace(value))
        {
            throw Invalid($"{path} must not be empty");
        }

        return value;
    }

    private static string Describe(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Undefined => "nothing",
        JsonValueKind.Null => "null",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Number => "a number",
        JsonValueKind.String => Quote(element.GetString()!),
        JsonValueKind.Array => "an array",
        JsonValueKind.Object => "an object",
        _ => element.ValueKind.ToString(),
    };

    /// <summary>Quotes a value for a message, shortened so a large document cannot flood a log.</summary>
    /// <param name="value">The value to quote.</param>
    /// <returns>The quoted and, if necessary, truncated value.</returns>
    private static string Quote(string value) =>
        value.Length <= 40 ? $"\"{value}\"" : $"\"{value[..40]}…\"";

    private static InvalidOperationException Invalid(string reason) => new(
        $"{FileName} is not a valid selectors document: {reason} (docs/scraping.md#selectors).");

    // The slug and numeric-source-id patterns of the schema. Generated, so no regular expression is
    // interpreted at run time.
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Slug();

    [GeneratedRegex("^[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Numeric();
}
