using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// Shape assertions on the Semgrep workflow.
/// </summary>
/// <remarks>
/// The scan is proven by running it, not by unit tests. What these pin are the
/// two properties a later edit could quietly invert: the advisory/blocking
/// split (findings must not fail the job, a broken scan must), and the
/// permission scope. Both are the kind of thing that gets "tidied" by someone
/// who reads <c>--no-error</c> as a mistake.
/// </remarks>
public sealed class SemgrepWorkflowTests
{
    private static string Workflow() =>
        File.ReadAllText(Path.Combine(RepoRoot.Path, ".github", "workflows", "semgrep.yml"));

    [Fact]
    public void Findings_are_advisory_and_a_broken_scan_is_not()
    {
        var workflow = Workflow();

        // `--no-error` is the whole mechanism: semgrep exits 0 on findings and
        // non-zero only when the scan failed. Remove it and every finding
        // becomes a blocking CI failure overnight.
        Assert.Contains("--no-error", workflow, StringComparison.Ordinal);

        // The corollary: the exit code must NOT be swallowed, or a crashed
        // engine passes as a clean scan.
        Assert.DoesNotContain("|| true", workflow);

        // And the output shape is checked, because a partial SARIF uploads
        // perfectly happily and reads as "nothing found".
        Assert.Contains("The scan must actually have run", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scan_that_loads_no_rules_fails()
    {
        // The failure this guards is the nastiest one available: the packs
        // fail to resolve, semgrep scans 1,289 files against zero rules,
        // reports zero findings, and the Security tab says "clean".
        var workflow = Workflow();

        Assert.Contains("The rule packs did not resolve", workflow, StringComparison.Ordinal);

        // A floor rather than an exact count, because the packs are rolling
        // (the engine is pinned; the rules are not). ~148 load today, so 100
        // catches a collapse without failing on ordinary rule churn.
        Assert.Contains("-lt 100", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Permissions_are_least_privilege_and_declared_per_job()
    {
        var workflow = Workflow();

        // Read-only default, so a job declaring nothing can write nothing.
        Assert.Contains("permissions:\n  contents: read", workflow, StringComparison.Ordinal);

        // SARIF upload needs exactly this one write and no others.
        Assert.Contains("security-events: write", workflow, StringComparison.Ordinal);

        foreach (var forbidden in new[] { "packages: write", "id-token: write", "contents: write", "pull-requests: write" })
        {
            Assert.DoesNotContain(forbidden, workflow);
        }
    }

    [Fact]
    public void The_engine_is_pinned_by_digest()
    {
        // Consistent with every other image in the repository (#52). A tag
        // would make the finding set move under #70 for reasons nobody
        // recorded.
        Assert.Contains("image: semgrep/semgrep@sha256:", Workflow(), StringComparison.Ordinal);
        Assert.DoesNotContain("image: semgrep/semgrep:", Workflow());
    }

    [Fact]
    public void Findings_reach_code_scanning_under_their_own_category()
    {
        var workflow = Workflow();

        Assert.Contains("github/codeql-action/upload-sarif", workflow, StringComparison.Ordinal);

        // Without a distinct category these interleave with CodeQL's alerts
        // and neither tool's results can be read on their own.
        Assert.Contains("category: semgrep", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void The_chosen_packs_are_the_ones_that_were_measured()
    {
        var workflow = Workflow();

        foreach (var pack in new[] { "p/csharp", "p/typescript", "p/secrets" })
        {
            Assert.Contains($"--config={pack}", workflow, StringComparison.Ordinal);
        }

        // p/react is deliberately absent: 4 rules against p/typescript's 74,
        // which is a strict superset. On this repository p/react finds
        // nothing while p/typescript finds four real wildcard-postMessage
        // sites. Re-adding it as a `--config` would be pure duplication.
        Assert.DoesNotContain("--config=p/react", workflow);
    }

    [Fact]
    public void Both_pull_requests_and_master_are_scanned()
    {
        var workflow = Workflow();

        Assert.Contains("pull_request", workflow, StringComparison.Ordinal);
        Assert.Contains("branches: [master]", workflow, StringComparison.Ordinal);
    }
}
