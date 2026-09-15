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

    /// <summary>
    /// Extracts one target's recipe, so an assertion about it cannot be
    /// satisfied by a matching line somewhere else in the Makefile.
    /// </summary>
    private static string Recipe(string target)
    {
        var makefile = File.ReadAllLines(Path.Combine(RepoRoot.Path, "Makefile"));
        var start = Array.FindIndex(makefile, l => l.StartsWith(target + ":", StringComparison.Ordinal));

        Assert.True(start >= 0, $"No `{target}:` target in the Makefile.");

        var body = makefile.Skip(start + 1)
            .TakeWhile(l => l.Length == 0 || l.StartsWith('\t') || l.StartsWith('#'))
            .ToList();

        Assert.NotEmpty(body);

        return string.Join('\n', body);
    }

    /// <summary>
    /// Every tier and every service carries an exact size pin (#453).
    /// </summary>
    /// <remarks>
    /// Per service as well as per tier: the tier total cannot see a test
    /// re-traited <em>out</em> of <c>Flowable</c>, because it stays in
    /// full-local — it just stops needing the engine. That is exactly how a
    /// live-engine oracle gets quietly defanged.
    /// </remarks>
    [Fact]
    public void Every_service_and_the_tier_total_carry_a_size_pin()
    {
        var tiers = Tiers();
        var services = tiers["AUTONATE_TIER_SERVICES"].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.NotEmpty(services);

        foreach (var service in services)
        {
            var key = "AUTONATE_TIER_COUNT_" + service.ToUpperInvariant();

            Assert.True(tiers.ContainsKey(key),
                $"{service} has no size pin. Add {key} to tests/tiers.env, or deleting every "
                + $"{service}-traited test is invisible.");

            Assert.True(int.TryParse(tiers[key], out var count) && count > 0,
                $"{key} is '{tiers[key]}'. A pin of zero passes against an empty tier.");
        }

        Assert.True(int.TryParse(tiers["AUTONATE_TIER_COUNT_FULL_LOCAL"], out var total) && total > 0,
            "AUTONATE_TIER_COUNT_FULL_LOCAL must be a positive integer.");
    }

    /// <summary>
    /// The pins agree with each other.
    /// </summary>
    /// <remarks>
    /// full-local is slim plus every service it includes, so its total has to
    /// exceed the services' alone — if it did not, the tier would consist
    /// entirely of service-traited tests, and one of these numbers was typed
    /// rather than measured.
    /// </remarks>
    [Fact]
    public void The_full_local_pin_exceeds_the_services_it_contains()
    {
        var tiers = Tiers();

        var flowable = int.Parse(tiers["AUTONATE_TIER_COUNT_FLOWABLE"]);
        var dapr = int.Parse(tiers["AUTONATE_TIER_COUNT_DAPR"]);
        var total = int.Parse(tiers["AUTONATE_TIER_COUNT_FULL_LOCAL"]);

        Assert.True(total > flowable + dapr,
            $"full-local is pinned at {total} but its services alone account for "
            + $"{flowable + dapr}. full-local is slim plus Flowable plus Dapr, so the total "
            + "must be strictly larger.");
    }

    /// <summary>
    /// The integrity check is wired into full-local, and runs even when red (#453).
    /// </summary>
    [Fact]
    public void Full_local_checks_its_own_integrity()
    {
        var script = Path.Combine(RepoRoot.Path, "infra", "tier-integrity.sh");

        Assert.True(File.Exists(script), "infra/tier-integrity.sh is missing, so a skipped or "
            + "deleted test in the full tier is invisible again (#453).");

        var recipe = Recipe("test-full-local");

        Assert.Contains("./infra/tier-integrity.sh", recipe, StringComparison.Ordinal);

        // It has to run after a red suite, the way backend-reconcile does: a lost
        // test otherwise hides behind a failure. The recipe accumulates rc rather
        // than letting the first non-zero abort it.
        Assert.Contains("|| rc=1", recipe, StringComparison.Ordinal);
        Assert.Contains("exit $$rc", recipe, StringComparison.Ordinal);
    }

    /// <summary>
    /// No <c>dotnet test</c> in the tier pipes straight into <c>tee</c> (#453).
    /// </summary>
    /// <remarks>
    /// A pipeline's exit status is its last command's, and there is no
    /// <c>set -o pipefail</c> to lean on — make runs recipes under
    /// <c>/bin/sh</c> and this Makefile sets no <c>SHELL</c>. So
    /// <c>dotnet test | tee</c> is always <c>tee</c>'s zero. The measured
    /// pre-fix behaviour was <c>Failed: 2, Passed: 340, Skipped: 1</c> with
    /// <c>make test-full-local</c> exiting <b>0</b> — the tier could not fail at
    /// all, which would have made a skips check pure theatre.
    /// </remarks>
    [Theory]
    [InlineData("test-full-local")]
    [InlineData("test-slim")]
    public void A_tier_never_pipes_a_test_run_straight_into_tee(string target)
    {
        var offenders = PipesIntoTee(Recipe(target));

        Assert.True(offenders.Count == 0,
            "These lines pipe a test run into tee, so the recipe reads tee's exit status and the "
            + "tier cannot go red:\n  " + string.Join("\n  ", offenders)
            + "\n\nCapture the status instead: { dotnet test ...; echo $? > f; } | tee log");
    }

    /// <summary>
    /// Recipe lines that run a test run through <c>tee</c> unguarded.
    /// </summary>
    /// <remarks>
    /// Shell comments are excluded. A make recipe line beginning <c>#</c> (or
    /// <c>@#</c>) is handed to the shell and discarded by it, so it cannot pipe
    /// anything — and the comment in <c>test-full-local</c> that explains this
    /// very defect necessarily contains the pattern it warns about. Excluding
    /// non-executable lines makes the guard more precise, not looser, which is
    /// why <see cref="The_guard_can_see_a_real_offender"/> exists: precision
    /// that quietly matched nothing would be the same failure in a better mood.
    /// </remarks>
    private static List<string> PipesIntoTee(string recipe) =>
        recipe.Split('\n')
            .Where(line => !line.TrimStart('\t', ' ', '@').StartsWith('#'))
            .Where(line => line.Contains("dotnet test", StringComparison.Ordinal)
                        && line.Contains("| tee", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void The_guard_can_see_a_real_offender()
    {
        // The exact line that shipped, and that made `make test-full-local`
        // exit 0 on `Failed: 2, Passed: 340, Skipped: 1`.
        Assert.Single(PipesIntoTee(
            "\t  dotnet test tests/AutoNate.Web.Tests --nologo 2>&1 | tee /tmp/n8-full-backend.log; \\"));

        // And the comment form, which is what the exclusion is for.
        Assert.Empty(PipesIntoTee(
            "\t@# status is its LAST command's, so `dotnet test | tee` is always tee's 0."));

        // The correct form stays clean, so the guard cannot be satisfied by
        // deleting the tee and losing the live output.
        Assert.Empty(PipesIntoTee(
            "\t  { dotnet test tests/AutoNate.Web.Tests --nologo 2>&1; echo $$? > /tmp/a.rc; } \\\n"
            + "\t    | tee /tmp/n8-full-backend.log; \\"));
    }
}
