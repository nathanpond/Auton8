using AutoNate.Web.Tests.Infrastructure;

namespace AutoNate.Web.Tests.Invariants;

/// <summary>
/// Finds a literal identifier in source, deliberately ignoring build output.
/// </summary>
/// <remarks>
/// The exclusions are the whole point. <c>wwwroot/</c> and <c>dist/</c> hold
/// bundled copies of the SPA's markers, so a naive repository-wide search finds
/// them even after the source has been renamed — the guard would pass while the
/// invariant was already broken. <c>App_Data/workflows/*.json</c> is saved BPMN
/// <i>data</i> rather than source and carries the namespace for the same
/// misleading reason.
/// </remarks>
internal static class SourceIdentifierScanner
{
    private static readonly string[] ExcludedSegments =
    [
        "/wwwroot/", "/dist/", "/node_modules/", "/bin/", "/obj/", "/target/",
        "/App_Data/", "/.git/", "RenameFixtures/",
    ];

    public static bool IsExcluded(string root, string path)
    {
        var relative = "/" + Path.GetRelativePath(root, path)
            .Replace(Path.DirectorySeparatorChar, '/');

        return ExcludedSegments.Any(s => relative.Contains(s, StringComparison.Ordinal));
    }

    /// <summary>
    /// Files under <paramref name="searchRoot"/> containing <paramref name="identifier"/>,
    /// excluding build output. <paramref name="scanned"/> reports how many files
    /// were examined — a scan that looked at nothing would otherwise report
    /// "not found" and read identically to a rename.
    /// </summary>
    public static IReadOnlyList<string> FindIn(
        string searchRoot, string identifier, string[] extensions, out int scanned)
    {
        var root = RepoRoot.Path;
        var absolute = Path.Combine(root, searchRoot);
        var matches = new List<string>();
        var count = 0;

        if (!Directory.Exists(absolute))
        {
            scanned = 0;
            return matches;
        }

        foreach (var file in Directory.EnumerateFiles(absolute, "*", SearchOption.AllDirectories))
        {
            if (IsExcluded(root, file) || !extensions.Contains(Path.GetExtension(file)))
            {
                continue;
            }

            count++;

            if (File.ReadAllText(file).Contains(identifier, StringComparison.Ordinal))
            {
                matches.Add(Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'));
            }
        }

        scanned = count;
        return matches;
    }
}
