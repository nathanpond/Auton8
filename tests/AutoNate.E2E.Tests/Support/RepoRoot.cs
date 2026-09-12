namespace AutoNate.E2E.Tests.Support;

/// <summary>
/// Locates the repository root from a test assembly's output directory (#332).
/// </summary>
/// <remarks>
/// <para>
/// Anchors on <c>AutoNate.sln</c>, which exists in every checkout. The obvious
/// alternative — walking up for a <c>.git</c> <em>directory</em> — is wrong, and
/// wrong in a way that hides: in a git worktree <c>.git</c> is a <em>file</em>
/// containing a <c>gitdir:</c> pointer, so <see cref="Directory.Exists"/> is
/// false all the way to <c>/</c>, the walk falls off the top, and a
/// <c>root!</c> dereference throws from a static initialiser.
/// </para>
/// <para>
/// That is not a hypothetical. <c>StartEventPlacementDifferentialTests</c>
/// resolved its manifest path that way, and <c>/n8-verify</c> runs in
/// worktrees — so all 33 of its cells collapsed to
/// <c>Failed: 1, Passed: 1, Total: 2</c> and the class never executed in any
/// verification round. Measured side by side at the same commit against the
/// same engine: 33/33 in a normal clone, 1 error in a worktree.
/// </para>
/// <para>
/// Mirrors <c>AutoNate.Web.Tests.Infrastructure.RepoRoot</c> and
/// <c>AutoNateE2EFixture.FindRepoRoot</c>; the three are not shared because the
/// test projects do not reference each other.
/// <c>RepoRootAnchorTests</c> in the backend suite fails the build if any test
/// source reintroduces the <c>.git</c>-anchored form.
/// </para>
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

        // Named, and naming where it started. The previous code dereferenced a
        // null root, so the symptom was a NullReferenceException inside a type
        // initialiser -- which reads as a broken fixture, not a broken path.
        throw new InvalidOperationException(
            $"Could not find AutoNate.sln walking up from {AppContext.BaseDirectory}.");
    }
}
