namespace TourSearch.UnitTests;

/// <summary>
/// Locates files of the working tree for tests that assert on <b>committed</b> data rather than on
/// a copy in the build output.
/// </summary>
/// <remarks>
/// The region catalog lives in two places that must agree — <c>selectors.json</c> at the root and
/// the migration's seed literal — and the test that guards them must read the committed file, not a
/// stale copy next to the test binary (NFR-001).
/// </remarks>
internal static class RepositoryFiles
{
    private const string SolutionFileName = "TourSearch.sln";

    /// <summary>The repository root, found by walking up from the test binary to the solution file.</summary>
    internal static string Root { get; } = FindRoot();

    /// <summary>Reads a committed file of the working tree.</summary>
    /// <param name="relativePath">Path relative to the repository root, using '/'.</param>
    /// <returns>The file's text.</returns>
    internal static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"No {SolutionFileName} above {AppContext.BaseDirectory}: these tests read the " +
                "committed files of the working tree and must run from the repository's build output.");
    }
}
