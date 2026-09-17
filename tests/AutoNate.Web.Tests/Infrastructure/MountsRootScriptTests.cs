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
            // Every checkout-relative bind mount, not only `./mounts/` (#513).
            // The narrow form left `./postgres/init`, `./scripts/...` and
            // `./keycloak/...` invisible -- and the init directory was observed
            // mounted from a worktree while the ownership check called the
            // stack ok, because that check reads the data mount alone. A
            // reset would then re-initialise the shared cluster from a
            // branch's SQL.
            // `source:destination`, so a command argument that happens to be a
            // relative path (`- ./daprd` in the sidecars' command arrays) is not
            // mistaken for a mount.
            .Where(entry => System.Text.RegularExpressions.Regex.IsMatch(
                entry.Line, @"^\s*-\s+\./[^\s:]+:"))
            // Tracked SOURCE files, deliberately read from the running
            // checkout: you want this branch's init SQL and seed data, not the
            // main checkout's. They are listed rather than pattern-matched so
            // that adding a fourth is a deliberate act with a reason attached.
            //
            // The residual hazard is worth knowing: `infra-reset` empties the
            // SHARED cluster, and the next start re-initialises it from
            // whichever checkout runs it -- so a reset from a worktree seeds the
            // shared stack from that branch's SQL. Both are :ro and only read on
            // an empty data directory.
            .Where(entry => !entry.Line.Contains("./postgres/init:", StringComparison.Ordinal)
                && !entry.Line.Contains("./scripts/bootstrap-jetstream.sh:", StringComparison.Ordinal)
                && !entry.Line.Contains("./keycloak/realm-export.json:", StringComparison.Ordinal))
            .Select(entry => $"docker-compose.yml:{entry.Number}: {entry.Line.Trim()}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These bind mounts resolve against the compose file's own directory, so in a "
            + "git worktree they point into that worktree rather than at the shared stack "
            + "(#505, #513). Route them through ${AUTONATE_MOUNTS_ROOT} — or, for a tracked "
            + "source directory that is deliberately read from this checkout, add it to the "
            + "allowlist in this test with the reason.\n  "
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
            // `:?`, not `:-`. The default was removed on purpose (#513): it was
            // the silent fallback every unconverted entry point could hit, so
            // compose now refuses to start without the variable rather than
            // quietly resolving `./mounts` against its own directory. Pinning
            // the `:?` form here means restoring a default is a failure, not a
            // detail — which is the whole reason the default went.
            .Matches(text, @"^\s*-\s+\$\{AUTONATE_MOUNTS_ROOT:\?[^}]*\}/", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Count;

        Assert.True(
            through == 7,
            $"expected 7 bind mounts through AUTONATE_MOUNTS_ROOT, found {through}. "
            + "If a service was added or removed, move this number in the same commit — "
            + "the point is that the change is visible, not that 7 is sacred.");
    }

    /// <summary>
    /// Every target that starts the stack reaches the ownership check (#517).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The check used to live only in <c>ensure-up.sh</c>, so <c>infra-up</c>,
    /// <c>infra-up-dashboard</c>, <c>app-container</c> and <c>keycloak-up</c>
    /// all brought the stack up with nothing in the way — and #513 then pointed
    /// Rider's Run button at <c>infra-up</c>, one of the unguarded four. The
    /// compose project name is <c>infra</c> on every path, so a second checkout
    /// replaces the first one's containers rather than starting its own.
    /// </para>
    /// <para>
    /// This asserts the property rather than the current spelling: any recipe
    /// that runs <c>compose … up</c> must reach <c>stack-ownership</c>, whether
    /// directly or through a prerequisite. A new target added later without one
    /// fails here instead of silently reopening #505.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_target_that_starts_the_stack_reaches_the_ownership_check()
    {
        var makefile = File.ReadAllLines(Path.Combine(RepoRoot.Path, "Makefile"));

        // target -> its declared prerequisites, for every rule in the file.
        var prerequisites = new Dictionary<string, string[]>(StringComparer.Ordinal);
        string? current = null;
        var recipes = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var line in makefile)
        {
            var rule = System.Text.RegularExpressions.Regex.Match(
                line, @"^(?<target>[A-Za-z0-9._-]+):(?!=)\s*(?<prereqs>.*)$");
            if (rule.Success)
            {
                current = rule.Groups["target"].Value;
                prerequisites[current] = rule.Groups["prereqs"].Value
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                recipes[current] = [];
                continue;
            }

            if (current is not null && line.StartsWith('\t')) recipes[current].Add(line);
            else if (line.Length > 0 && !char.IsWhiteSpace(line[0])) current = null;
        }

        bool Reaches(string target, HashSet<string> seen)
        {
            if (target == "stack-ownership") return true;
            if (!seen.Add(target)) return false;
            return prerequisites.TryGetValue(target, out var prereqs)
                && prereqs.Any(prereq => Reaches(prereq, seen));
        }

        // A recipe line that brings containers up. `down`, `rm`, `ps` and `logs`
        // are not starts and are deliberately not matched.
        var starters = recipes
            .Where(entry => entry.Value.Any(line =>
                System.Text.RegularExpressions.Regex.IsMatch(line, @"\$\(COMPOSE\).*\bup\b")))
            .Select(entry => entry.Key)
            .ToList();

        Assert.True(
            starters.Count > 0,
            "no Makefile recipe appears to start the stack — this guard has stopped "
            + "looking at anything, which is the failure it exists to prevent.");

        var unguarded = starters.Where(target => !Reaches(target, [])).ToList();

        Assert.True(
            unguarded.Count == 0,
            "These targets start the stack without reaching `stack-ownership`, so they can "
            + "replace another checkout's running containers with no warning (#505, #517):\n  "
            + string.Join("\n  ", unguarded)
            + "\n\nAdd `stack-ownership` to the target's prerequisites, or depend on "
            + "`infra-prepare`, which already does.");
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
        //
        // Scoped to the FUNCTION, not the file (#513/#514). The previous form
        // searched the whole script including comments, so gutting the body to
        // `return 0` and leaving `# was: docker inspect ... /var/lib/postgresql/data`
        // anywhere in the file kept all six facts green -- the
        // comment-satisfiable pattern this repo has now filed four times.
        var functionStart = Array.FindIndex(script, line =>
            line.StartsWith("check_stack_ownership()", StringComparison.Ordinal));
        Assert.True(functionStart >= 0, "check_stack_ownership is no longer defined.");
        var functionEnd = Array.FindIndex(script, functionStart, line => line == "}");
        Assert.True(functionEnd > functionStart, "could not find the end of check_stack_ownership.");

        var body = string.Join("\n", script[functionStart..(functionEnd + 1)]
            .Where(line => !line.TrimStart().StartsWith('#')));

        // It delegates to the shared script now (#517) rather than carrying a
        // second copy of the check — the two copies had already drifted, one
        // hard-failing on an unreadable mount where the other returned 0 (#519).
        // So the "not a no-op" property is asserted in two halves: this function
        // must actually invoke the script, and the script must actually inspect
        // a container's mount.
        Assert.Contains("assert-stack-ownership.sh", body, StringComparison.Ordinal);

        var shared = File.ReadAllLines(
                Path.Combine(RepoRoot.Path, "infra", "assert-stack-ownership.sh"))
            .Where(line => !line.TrimStart().StartsWith('#'))
            .ToArray();
        var sharedBody = string.Join("\n", shared);

        Assert.Contains("docker inspect", sharedBody, StringComparison.Ordinal);
        Assert.Contains("/var/lib/postgresql/data", sharedBody, StringComparison.Ordinal);
        // And it must be able to REFUSE: a script that only ever exits 0 would
        // satisfy everything above while checking nothing.
        Assert.Contains("exit 1", sharedBody, StringComparison.Ordinal);
    }
}
