using System.Text.RegularExpressions;
using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// No test may resolve the repository root by looking for a <c>.git</c>
/// directory (#332).
/// </summary>
/// <remarks>
/// <para>
/// In a git worktree <c>.git</c> is a <em>file</em> holding a <c>gitdir:</c>
/// pointer, not a directory. A walk testing <c>Directory.Exists(".git")</c>
/// therefore never matches, runs off the top of the filesystem, and throws —
/// usually from a static initialiser, where it surfaces as a
/// <c>TypeInitializationException</c> that reads like a broken fixture.
/// </para>
/// <para>
/// The reason this is worth a mechanical guard rather than a code review note:
/// <c>/n8-verify</c> runs in worktrees. When
/// <c>StartEventPlacementDifferentialTests</c> had this bug, its 33 cells
/// collapsed to <c>Failed: 1, Passed: 1, Total: 2</c> and it silently never ran
/// in any verification round — while passing 33/33 for anyone who ran it in a
/// normal clone. A defect that only appears in the environment that checks for
/// defects will not be found by checking.
/// </para>
/// <para>
/// What this does NOT cover: a root walk that is wrong some other way (anchored
/// on a file that does not exist, or that exists in more than one ancestor). It
/// pins the one form that has actually shipped broken.
/// </para>
/// </remarks>
public sealed class RepoRootAnchorTests
{
    // Any directory-existence test against a ".git" marker, however it is
    // spelled. The first version matched one literal form and three natural
    // rephrasings of the identical defect sailed through it (#341):
    //
    //   const string GitMarker = ".git"; ... Directory.Exists(Combine(dir, GitMarker))
    //   Directory.Exists(dir.FullName + "/.git")
    //   new DirectoryInfo(Combine(dir, ".git")).Exists
    //
    // So this matches on the two halves separately -- a ".git" literal or a
    // marker constant holding one, anywhere near a directory-existence test --
    // rather than on one arrangement of them. The File.Exists form stays fine,
    // and so does Directory.Exists on any other path.
    private static readonly Regex GitDirectoryAnchor = new(
        @"Directory\.Exists\([^;]{0,200}?(?:\.git""|GitMarker|GitDir)"
        + @"|new\s+DirectoryInfo\([^;]{0,200}?(?:\.git""|GitMarker|GitDir)[^;]{0,80}?\)\s*\.Exists",
        RegexOptions.Compiled);


    [Fact]
    public void No_test_source_anchors_the_repo_root_on_a_git_directory()
    {
        // Both trees. A helper under src/ resolving the root this way breaks in a
        // worktree exactly as a test one does, and the first version scanned only
        // tests/ (#341).
        var offenders = new[] { "tests", "src" }
            .Select(dir => Path.Combine(RepoRoot.Path, dir))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            // This file names the pattern in order to forbid it.
            .Where(path => !path.EndsWith(nameof(RepoRootAnchorTests) + ".cs", StringComparison.Ordinal))
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .Where(file => GitDirectoryAnchor.IsMatch(file.Text))
            .Select(file => Path.GetRelativePath(RepoRoot.Path, file.Path))
            .Order()
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These test sources locate the repository root by looking for a `.git` DIRECTORY, "
            + "which does not exist in a git worktree (there it is a file). They will throw "
            + "instead of running, and `/n8-verify` works in worktrees.\n  "
            + string.Join("\n  ", offenders)
            + "\n\nAnchor on AutoNate.sln instead — see AutoNate.E2E.Tests.Support.RepoRoot "
            + "or AutoNate.Web.Tests.Infrastructure.RepoRoot.");
    }

    [Fact]
    public void The_guard_can_see_the_pattern_it_forbids()
    {
        // Without this, a regex that matches nothing -- a renamed method, an
        // escaping slip -- reports a clean tree forever. The string below is the
        // exact shape that shipped broken in StartEventPlacementDifferentialTests.
        const string TheBugAsItShipped =
            """while (root is not null && !Directory.Exists(Path.Combine(root.FullName, ".git")))""";

        Assert.Matches(GitDirectoryAnchor, TheBugAsItShipped);

        // The three rephrasings that beat the first version of this regex. Each
        // is the same defect and each is false in a worktree (#341).
        Assert.Matches(GitDirectoryAnchor,
            """Directory.Exists(Path.Combine(dir.FullName, GitMarker))""");
        Assert.Matches(GitDirectoryAnchor,
            """Directory.Exists(root.FullName + "/.git")""");
        Assert.Matches(GitDirectoryAnchor,
            """new DirectoryInfo(Path.Combine(dir.FullName, ".git")).Exists""");

        // And it does not fire on the correct forms, so the guard cannot be
        // satisfied by deleting legitimate code.
        Assert.DoesNotMatch(GitDirectoryAnchor, """File.Exists(Path.Combine(dir.FullName, ".git"))""");
        Assert.DoesNotMatch(GitDirectoryAnchor, """Directory.Exists(Path.Combine(dir.FullName, "src", "shared"))""");
    }
}
