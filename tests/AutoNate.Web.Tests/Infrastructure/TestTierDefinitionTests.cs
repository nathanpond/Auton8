using System.Reflection;
using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// The three test tiers have one definition, and it is the one both readers use (#472).
/// </summary>
/// <remarks>
/// <para>
/// "CI" used to mean one thing while the repo had three: what GitHub runs, what
/// a developer can run against real services, and what needs Keycloak. The
/// boundary is the <c>RequiresService</c> trait, and the filters that express it
/// live in <c>tests/tiers.env</c> — read by the Makefile with <c>include</c> and
/// by <c>ci.yml</c> through <c>$GITHUB_ENV</c>.
/// </para>
/// <para>
/// This exists because the previous arrangement had already drifted and nothing
/// noticed: <c>BackendShardingWorkflowTests</c> pinned the e2e filter as a
/// literal asserting <b>two</b> services while the workflow filtered
/// <b>three</b>, and it passed because <c>Assert.Contains</c> is a substring
/// check. A guard that can be satisfied by a prefix of the truth is not a guard.
/// </para>
/// </remarks>
public sealed class TestTierDefinitionTests
{
    private static string TiersPath => Path.Combine(RepoRoot.Path, "tests", "tiers.env");

    private static IReadOnlyDictionary<string, string> Tiers()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(TiersPath))
        {
            if (!line.StartsWith("AUTONATE_TIER_", StringComparison.Ordinal)) continue;

            var split = line.IndexOf('=');
            if (split < 0) continue;

            values[line[..split]] = line[(split + 1)..];
        }

        return values;
    }

    [Fact]
    public void The_tier_file_defines_every_tier()
    {
        var tiers = Tiers();

        Assert.True(tiers.Count >= 4, $"tests/tiers.env parsed {tiers.Count} definitions. If it "
            + "parsed none, this guard is reporting a clean bill of health against nothing.");

        Assert.Contains("AUTONATE_TIER_SERVICES", tiers);
        Assert.Contains("AUTONATE_TIER_SLIM_FILTER", tiers);
        Assert.Contains("AUTONATE_TIER_FULL_LOCAL_FILTER", tiers);
        Assert.Contains("AUTONATE_TIER_FULL_KEYCLOAK_FILTER", tiers);
    }

    /// <summary>
    /// The slim filter excludes exactly the known services — no more, no fewer.
    /// </summary>
    /// <remarks>
    /// VSTest has no trait-absence operator, so "no trait" has to be written as
    /// the negated conjunction of every service. That makes the service list and
    /// the filter two statements of one fact, and the failure mode is silent: a
    /// service added to the list but not the filter moves its tests into slim.
    /// </remarks>
    [Fact]
    public void The_slim_filter_is_derived_from_the_service_list()
    {
        var tiers = Tiers();
        var services = tiers["AUTONATE_TIER_SERVICES"].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var expected = string.Join('&', services.Select(s => $"RequiresService!={s}"));

        Assert.Equal(expected, tiers["AUTONATE_TIER_SLIM_FILTER"]);
    }

    [Fact]
    public void The_workflow_takes_its_filter_from_the_tier_file()
    {
        var workflow = File.ReadAllText(
            Path.Combine(RepoRoot.Path, ".github", "workflows", "ci.yml"));

        Assert.Contains("$AUTONATE_TIER_SLIM_FILTER", workflow, StringComparison.Ordinal);

        // The literal must be gone, or there are two definitions again and one
        // of them is the one that already drifted.
        Assert.DoesNotContain("--filter \"RequiresService!=", workflow, StringComparison.Ordinal);

        // And the file must actually be loaded, or the filter expands to empty
        // and the job silently runs every test including the traited ones.
        Assert.Contains("tests/tiers.env", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void The_makefile_takes_its_filters_from_the_tier_file()
    {
        var makefile = File.ReadAllText(Path.Combine(RepoRoot.Path, "Makefile"));

        Assert.Contains("include tests/tiers.env", makefile, StringComparison.Ordinal);
        Assert.Contains("$(AUTONATE_TIER_SLIM_FILTER)", makefile, StringComparison.Ordinal);
        Assert.Contains("$(AUTONATE_TIER_FULL_LOCAL_FILTER)", makefile, StringComparison.Ordinal);
    }

    /// <summary>
    /// full-local stands its services up and fails closed when it cannot (#473).
    /// </summary>
    [Fact]
    public void Full_local_preflights_before_it_runs()
    {
        var makefile = File.ReadAllText(Path.Combine(RepoRoot.Path, "Makefile"));
        var preflight = Path.Combine(RepoRoot.Path, "infra", "tier-preflight.sh");

        Assert.True(File.Exists(preflight), "infra/tier-preflight.sh is missing, so full-local "
            + "has nothing that names a missing service (#473).");

        Assert.Contains("./infra/tier-preflight.sh", makefile, StringComparison.Ordinal);

        var script = File.ReadAllText(preflight);

        // It must fail, not warn. A tier that skips what it cannot reach reports
        // success for the wrong reason, which is the whole subject of M4c.
        Assert.Contains("exit 1", script, StringComparison.Ordinal);

        // And it must name what it tried, or "did not become ready" is all a
        // developer gets -- which is what `ensure-up.sh` already says.
        Assert.Contains("tried", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing still tells a developer to run the old recipe (#473).
    /// </summary>
    /// <remarks>
    /// Three documents carried one: <c>CONTRIBUTING.md</c> — whose "say so in the
    /// PR so it gets run somewhere that has them" <em>was</em> the process the
    /// tiers replace — <c>docs/DEVELOPMENT.md</c>, and the E2E README.
    /// </remarks>
    [Theory]
    [InlineData("CONTRIBUTING.md")]
    [InlineData("docs/DEVELOPMENT.md")]
    public void The_contributor_docs_point_at_the_named_tiers(string relative)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot.Path, relative.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains("make test-slim", text, StringComparison.Ordinal);
        Assert.DoesNotContain("say so in the PR so it gets run", text, StringComparison.Ordinal);
    }
}
