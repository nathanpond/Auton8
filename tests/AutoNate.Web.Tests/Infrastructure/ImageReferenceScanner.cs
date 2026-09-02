using System.Text.RegularExpressions;

namespace AutoNate.Web.Tests.Infrastructure;

internal sealed record ImageReference(string File, int Line, string Reference)
{
    /// <summary>
    /// A reference is pinned when it names a content digest. A tag — even an
    /// exact-looking one like <c>1.17.5</c> — is a mutable pointer the registry
    /// can move.
    /// </summary>
    public bool IsPinned => Reference.Contains("@sha256:", StringComparison.Ordinal);

    /// <summary>
    /// <c>scratch</c> is not an image and has no digest to pin.
    /// </summary>
    public bool IsExempt => Reference is "scratch";
}

internal static class ImageReferenceScanner
{
    private static readonly Regex FromLine =
        new(@"^\s*FROM\s+(?<image>\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ComposeImageLine =
        new(@"^\s*image:\s*(?<image>\S+)", RegexOptions.Compiled);

    public static IReadOnlyList<string> DiscoverDockerfiles()
    {
        var root = RepoRoot.Path;
        return Directory
            .EnumerateFiles(root, "Dockerfile*", SearchOption.AllDirectories)
            .Where(p =>
            {
                var rel = Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/');
                return !rel.Contains("/node_modules/", StringComparison.Ordinal)
                    && !rel.Contains("/bin/", StringComparison.Ordinal)
                    && !rel.Contains("/obj/", StringComparison.Ordinal)
                    && !rel.Contains("/target/", StringComparison.Ordinal)
                    && !rel.Contains("ImageFixtures/", StringComparison.Ordinal);
            })
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<ImageReference> ScanDockerfile(string path) =>
        Scan(path, FromLine);

    public static IReadOnlyList<ImageReference> ScanCompose(string path) =>
        Scan(path, ComposeImageLine);

    private static IReadOnlyList<ImageReference> Scan(string path, Regex pattern)
    {
        var relative = Path.GetRelativePath(RepoRoot.Path, path)
            .Replace(Path.DirectorySeparatorChar, '/');
        var results = new List<ImageReference>();
        var lines = File.ReadAllLines(path);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var match = pattern.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var image = match.Groups["image"].Value;

            // `FROM x AS y` and multi-stage `FROM builder` where `builder` is a
            // previously named stage rather than a registry image.
            if (image.Equals("AS", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add(new ImageReference(relative, i + 1, image));
        }

        return results;
    }

    /// <summary>
    /// Stage names defined by <c>FROM ... AS name</c>, which a later
    /// <c>FROM name</c> refers to. These are not registry images.
    /// </summary>
    public static ISet<string> StageNames(string path)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(path))
        {
            var m = Regex.Match(line, @"^\s*FROM\s+\S+\s+AS\s+(?<name>\S+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                names.Add(m.Groups["name"].Value);
            }
        }

        return names;
    }
}
