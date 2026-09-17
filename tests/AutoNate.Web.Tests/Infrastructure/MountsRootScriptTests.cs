using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// Every worktree must resolve the compose bind-mount root to the same
/// directory — the main checkout's (#505).
/// </summary>
/// <remarks>
/// <para>
/// <c>infra/mounts/**</c> is gitignored, so it does not exist in a fresh
/// <c>git worktree</c>. Compose resolves relative bind mounts against the
/// compose file's own directory, so <c>make test-full-local</c> from a worktree
/// stood the stack up on an <em>empty</em> data directory — and, because the
/// project name is <c>infra</c> either way, it replaced the developer's stack
/// rather than starting a second one. Postgres became a brand-new cluster and
/// Flowable came up with no schema (<c>relation "act_ru_job" does not exist</c>),
/// after which 161 of 407 E2E tests failed with <c>"The workflow engine refused
/// this workflow"</c> — a message shaped exactly like a product regression.
/// </para>
/// <para>
/// This runs in the slim tier: <c>mounts-root.sh</c> needs git and nothing else,
/// no Docker and no stack. That is deliberate — <c>/n8-verify</c> runs in
/// worktrees, so the regression this guards against would otherwise reappear in
/// the one environment that checks for regressions.
/// </para>
/// <para>
/// What this does NOT cover: whether the running stack actually serves that
/// root. That is <c>tier-preflight.sh</c>'s ownership check, which needs a live
/// container to inspect and so cannot run here; the structural half of it is
/// pinned below.
/// </para>
/// </remarks>
public sealed class MountsRootScriptTests
{
    private static string ScriptPath => Path.Combine(RepoRoot.Path, "infra", "mounts-root.sh");

    private static string ComposePath => Path.Combine(RepoRoot.Path, "infra", "docker-compose.yml");

    // Matching TierIntegrityScriptTests and PreflightScriptTests: an early
    // return rather than a Skip attribute, because a skipped test is precisely
    // what the tier gates count as a failure.
    private static bool ShellAvailable => !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static (int ExitCode, string Output) RunFrom(string workingDirectory)
    {
        var psi = new ProcessStartInfo("/bin/bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        psi.ArgumentList.Add(ScriptPath);
        // The parent's value must not leak in: the script prefers its own
        // resolution and this test is about that resolution.
        psi.Environment.Remove("AUTONATE_MOUNTS_ROOT");

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.Trim());
    }

    /// <summary>
    /// The main checkout, located by a DIFFERENT git mechanism than the one
    /// under test.
    /// </summary>
    /// <remarks>
    /// These tests previously built their expectation from
    /// <c>RepoRoot.Path</c>, which is whatever checkout the test assembly is
    /// running in — so in a worktree they compared the script's correct answer
    /// against the worktree's own path and failed. `make test-full-local` was
    /// therefore red from every worktree, on the guard rather than on the thing
    /// guarded, and `/n8-verify` runs in worktrees (#515).
    ///
    /// `git worktree list --porcelain` reports the main worktree first, from
    /// inside a linked worktree as well as from the main checkout. Using it
    /// rather than <c>--git-common-dir</c> keeps the oracle independent of the
    /// mechanism `mounts-root.sh` uses, so this cannot pass by agreeing with a
    /// bug.
    /// </remarks>
    private static string MainCheckout()
    {
        var (code, output) = RunGit(RepoRoot.Path, "worktree", "list", "--porcelain");
        Assert.True(code == 0, $"could not list worktrees: {output}");

        var first = output.Split('\n')
            .FirstOrDefault(line => line.StartsWith("worktree ", StringComparison.Ordinal));
        Assert.True(first is not null, $"`git worktree list --porcelain` named no worktree:\n{output}");

        return first!["worktree ".Length..].Trim();
    }

    private static string MainCheckoutMountsRoot() =>
        Path.Combine(MainCheckout(), "infra", "mounts");

    private static (int ExitCode, string Output) RunGit(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.Trim());
    }

    [Fact]
    public void A_worktree_resolves_the_mounts_root_to_the_main_checkout()
    {
        if (!ShellAvailable) return;

        var expected = MainCheckoutMountsRoot();

        // A real linked worktree, because that is the failing configuration.
        // Asserting against a simulated one would prove nothing about how git
        // reports --git-common-dir from inside a real one.
        var worktree = Path.Combine(
            Path.GetTempPath(), "mounts-root-" + Guid.NewGuid().ToString("N")[..8]);
        var (addCode, addOutput) = RunGit(RepoRoot.Path, "worktree", "add", "--detach", "-q", worktree, "HEAD");
        Assert.True(addCode == 0, $"could not create the worktree this test is about: {addOutput}");

        try
        {
            var (code, output) = RunFrom(worktree);

            Assert.True(code == 0, $"mounts-root.sh failed from a worktree: {output}");

            // Compare resolved paths: /tmp is a symlink to /private/tmp on
            // macOS, so two names for one directory would otherwise differ.
            Assert.Equal(
                new DirectoryInfo(expected).FullName,
                new DirectoryInfo(output).FullName);

            // And the thing that actually went wrong: the answer must not be
            // inside the worktree. Without this, a script that returned a
            // correct-looking relative path would pass the equality above on a
            // machine where the two happen to coincide.
            Assert.False(
                output.StartsWith(new DirectoryInfo(worktree).FullName, StringComparison.Ordinal),
                $"the mounts root resolved inside the worktree ({output}); "
                + "that is the empty directory the stack was stood up in (#505).");
        }
        finally
        {
            RunGit(RepoRoot.Path, "worktree", "remove", "--force", worktree);
        }
    }

    [Fact]
    public void The_main_checkout_resolves_to_its_own_mounts_directory()
    {
        if (!ShellAvailable) return;

        // The complement of the above. A script hard-wired to "always return
        // some other directory" would satisfy the worktree case while breaking
        // every ordinary run, so the normal path is asserted rather than assumed.
        var mainCheckout = MainCheckout();
        var (code, output) = RunFrom(mainCheckout);

        Assert.True(code == 0, $"mounts-root.sh failed from the main checkout: {output}");
        Assert.Equal(
            new DirectoryInfo(Path.Combine(mainCheckout, "infra", "mounts")).FullName,
            new DirectoryInfo(output).FullName);
    }

    [Fact]
    public void It_answers_the_same_from_a_subdirectory()
    {
        if (!ShellAvailable) return;

        // `git rev-parse --git-common-dir` without --path-format=absolute is
        // relative to the CWD: `.git` from the root but `../.git` from infra/.
        // Composing that with ".." by hand is wrong from anywhere but the top,
        // and the tier's own recipes invoke scripts from both places.
        var (fromRoot, rootOutput) = RunFrom(RepoRoot.Path);
        var (fromInfra, infraOutput) = RunFrom(Path.Combine(RepoRoot.Path, "infra"));

        Assert.Equal(0, fromRoot);
        Assert.Equal(0, fromInfra);
        Assert.Equal(rootOutput, infraOutput);
    }

    [Fact]
    public void No_compose_bind_mount_uses_a_bare_relative_mounts_path()
    {
        // The defect in one line. A bare `./mounts/...` is resolved against the
        // compose file's directory, which is the worktree's — so one of these
        // reintroduces #505 for whichever service carries it, while every other
        // service keeps using the shared root and nothing looks obviously wrong.
        var offenders = File.ReadAllLines(ComposePath)
            .Select((line, index) => (Line: line, Number: index + 1))
            .Where(entry => System.Text.RegularExpressions.Regex.IsMatch(
                entry.Line, @"^\s*-\s+\./mounts/"))
            .Select(entry => $"docker-compose.yml:{entry.Number}: {entry.Line.Trim()}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These bind mounts resolve against the compose file's own directory, so in a "
            + "git worktree they point at an empty path and the stack comes up on a blank "
            + "cluster (#505). Use ${AUTONATE_MOUNTS_ROOT:-./mounts} instead.\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Every_persistent_bind_mount_goes_through_the_shared_root()
    {
        // A count, not a boolean. The check above only says nothing is spelled
        // the old way; this says the new spelling is actually carrying all of
        // them, so a mount deleted outright — or a new service added with a
        // named volume nobody meant — shows up as a number that moved.
        var text = File.ReadAllText(ComposePath);
        var through = System.Text.RegularExpressions.Regex
            .Matches(text, @"^\s*-\s+\$\{AUTONATE_MOUNTS_ROOT:-\./mounts\}/", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Count;

        Assert.True(
            through == 7,
            $"expected 7 bind mounts through AUTONATE_MOUNTS_ROOT, found {through}. "
            + "If a service was added or removed, move this number in the same commit — "
            + "the point is that the change is visible, not that 7 is sacred.");
    }

    [Fact]
    public void The_tier_preflight_checks_stack_ownership_before_it_probes_ports()
    {
        // Ports answering is the wrong question when the stack belongs to a
        // different checkout; it answers every probe and is still wrong. The
        // ordering matters because the ownership mismatch is the diagnosis —
        // reporting it after six green port probes buries it.
        var script = File.ReadAllLines(
            Path.Combine(RepoRoot.Path, "infra", "tier-preflight.sh"));

        var call = Array.FindIndex(script, line =>
            line.TrimStart().StartsWith("check_stack_ownership", StringComparison.Ordinal)
            && !line.TrimStart().StartsWith("#", StringComparison.Ordinal)
            && !line.Contains('(', StringComparison.Ordinal));

        var firstProbe = Array.FindIndex(script, line =>
            line.TrimStart().StartsWith("probe_port", StringComparison.Ordinal)
            && !line.Contains("()", StringComparison.Ordinal));

        Assert.True(call >= 0, "tier-preflight.sh no longer calls check_stack_ownership.");
        Assert.True(firstProbe >= 0, "tier-preflight.sh no longer probes any port.");
        Assert.True(
            call < firstProbe,
            $"check_stack_ownership is called at line {call + 1}, after the first port probe "
            + $"at line {firstProbe + 1}. The ownership mismatch is the diagnosis and belongs first (#505).");

        // And it must actually read a container's mount, not merely be present:
        // a function that returns 0 unconditionally satisfies the ordering
        // check above while checking nothing.
        var body = string.Join("\n", script);
        Assert.Contains("docker inspect autonate-postgres", body, StringComparison.Ordinal);
        Assert.Contains("/var/lib/postgresql/data", body, StringComparison.Ordinal);
    }
}
