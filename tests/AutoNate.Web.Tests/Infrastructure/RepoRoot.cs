namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// Locates the repository root from a test assembly's output directory.
/// </summary>
/// <remarks>
/// Infrastructure guards assert on files that live in the repository rather
/// than on anything copied into <c>bin/</c> — deliberately, because a guard
/// reading a build-copied duplicate passes while the source it is supposed to
/// protect has already drifted.
///
/// Mirrors <c>AutoNateE2EFixture.FindRepoRoot</c>; the two are not shared
/// because the test projects do not reference each other.
/// </remarks>
internal static class RepoRoot
{
    private static readonly Lazy<string> Resolved = new(Find);

    public static string Path => Resolved.Value;

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "AutoNate.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find AutoNate.sln walking up from {AppContext.BaseDirectory}.");
    }
}
