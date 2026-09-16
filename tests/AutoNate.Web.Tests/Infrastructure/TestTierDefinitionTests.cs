using System.Text.RegularExpressions;
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

    /// <summary>
    /// A tier filter written out by hand, in any quoting (#490).
    /// </summary>
    /// <remarks>
    /// `tests/tiers.env` says nothing else may spell a tier filter. The first
    /// absence check matched one spelling — <c>--filter "RequiresService!=</c> —
    /// so single quotes, no quotes, or an intermediate variable all walked past
    /// it. This matches the trait comparison itself.
    /// </remarks>
    /// <summary>A <c>RequiresService</c> trait, however qualified (#490).</summary>
    private static readonly Regex ServiceTrait =
        new(@"Trait\s*\(\s*""RequiresService""", RegexOptions.Compiled);

    private static readonly Regex HandWrittenTierFilter =
        new(@"RequiresService\s*!=", RegexOptions.Compiled);

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
        // of them is the one that already drifted. ANY quoting (#490): the
        // original checked the double-quoted spelling only, so
        // `--filter 'RequiresService!=Flowable'` walked past it.
        Assert.DoesNotMatch(HandWrittenTierFilter, workflow);

        // And the LOADER line, not a mention of the path (#490). ci.yml names
        // tests/tiers.env in four places, so asserting the path left three ways
        // to delete the one that matters -- and without it every filter expands
        // to empty and the job runs the traited tests too.
        Assert.Contains("grep -E '^AUTONATE_TIER_' tests/tiers.env", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void The_makefile_takes_its_filters_from_the_tier_file()
    {
        var makefile = File.ReadAllText(Path.Combine(RepoRoot.Path, "Makefile"));

        Assert.Contains("include tests/tiers.env", makefile, StringComparison.Ordinal);
        Assert.Contains("$(AUTONATE_TIER_SLIM_FILTER)", makefile, StringComparison.Ordinal);
        Assert.Contains("$(AUTONATE_TIER_FULL_LOCAL_FILTER)", makefile, StringComparison.Ordinal);

        // ABSENCE too (#490). This was presence-only, so a second, drifting copy
        // of a filter in the Makefile was unguarded -- and "one definition, two
        // readers" is the story's whole claim, with only one reader checked.
        Assert.DoesNotMatch(HandWrittenTierFilter, makefile);
    }

    /// <summary>
    /// full-local stands its services up and names what it cannot reach (#473, #487).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be called <c>Full_local_preflights_before_it_runs</c> and
    /// asserted nothing about "before" — it read the whole Makefile and checked
    /// that the preflight was mentioned somewhere in it. The name was also
    /// describing something untrue: <c>infra-ensure</c> was a prerequisite, and
    /// make builds prerequisites before the recipe, so the preflight ran
    /// <em>after</em> the 120-second compose wait it claimed to precede.
    /// </para>
    /// <para>
    /// The ordering it asserted for cannot work: <c>ensure-up.sh</c> is what
    /// starts the services, so probing ahead of it fails on any cold machine.
    /// What is checkable, and what actually helps, is that a failed compose-up
    /// hands off to the preflight for the named diagnosis.
    /// </para>
    /// </remarks>
    [Fact]
    public void Full_local_names_the_service_it_cannot_reach()
    {
        var preflight = Path.Combine(RepoRoot.Path, "infra", "tier-preflight.sh");

        Assert.True(File.Exists(preflight), "infra/tier-preflight.sh is missing, so full-local "
            + "has nothing that names a missing service (#473).");

        var recipe = Recipe("test-full-local");

        // In the RECIPE, not anywhere in the Makefile. The class defines this
        // helper precisely so an assertion cannot be satisfied by a matching
        // line in some other target -- and this test was the one not using it.
        Assert.Contains("./infra/tier-preflight.sh", recipe, StringComparison.Ordinal);

        // Twice: once on ensure-up's failure path, where it turns "did not
        // become ready" into a service and an endpoint, and once after the
        // stack is up, where a container can be healthy with a dead endpoint
        // behind it.
        var runs = recipe.Split("./infra/tier-preflight.sh").Length - 1;
        Assert.True(runs >= 2,
            $"The recipe runs the preflight {runs} time(s). It needs both: the failure hand-off "
            + "after ensure-up (otherwise a dead service still reports only `Compose stack did "
            + "not become ready`), and the post-up probe (#487).");

        // And `infra-ensure` must NOT be a prerequisite -- as one it is ordered
        // ahead of everything in the recipe, which is how the hand-off was lost.
        var declaration = File.ReadAllLines(Path.Combine(RepoRoot.Path, "Makefile"))
            .First(l => l.StartsWith("test-full-local:", StringComparison.Ordinal));

        Assert.DoesNotContain("infra-ensure", declaration, StringComparison.Ordinal);

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
    /// The pins are arithmetically related, so they must actually agree (#507).
    /// </summary>
    /// <remarks>
    /// <para>
    /// full-local's E2E run is the slim E2E set plus the Flowable-traited tests
    /// plus the Dapr one — every E2E test except Keycloak's. So
    /// <c>SLIM_E2E + FLOWABLE + DAPR</c> is not merely a bound on
    /// <c>FULL_LOCAL</c>, it is the same number by construction.
    /// </para>
    /// <para>
    /// The inequality above accepts any total larger than 205, so
    /// <c>FLOWABLE=150</c> passes it while quietly excusing 54 missing engine
    /// tests. Only a full-local run would notice, and full-local is not a merge
    /// gate — whereas this identity is pure arithmetic over a checked-in file
    /// and is therefore decidable in slim, on every push.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_tier_pins_add_up()
    {
        var tiers = Tiers();

        var slimE2E = int.Parse(tiers["AUTONATE_TIER_COUNT_SLIM_E2E"]);
        var flowable = int.Parse(tiers["AUTONATE_TIER_COUNT_FLOWABLE"]);
        var dapr = int.Parse(tiers["AUTONATE_TIER_COUNT_DAPR"]);
        var fullLocal = int.Parse(tiers["AUTONATE_TIER_COUNT_FULL_LOCAL"]);

        Assert.True(
            slimE2E + flowable + dapr == fullLocal,
            $"the E2E pins disagree: slim {slimE2E} + Flowable {flowable} + Dapr {dapr} "
            + $"= {slimE2E + flowable + dapr}, but full-local is pinned at {fullLocal}. "
            + "full-local is every E2E test except Keycloak's, so these are the same "
            + "number. Whichever pin moved, move the others in the same commit.");
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

    /// <summary>
    /// Slim polices itself inside GitHub's own gate (#476).
    /// </summary>
    /// <remarks>
    /// <para>
    /// full got a zero-skips check and an exact pin in #453; the tier that
    /// actually blocks merges got neither, and the tier story's own wording
    /// advertised the escape as a feature — "adding a traited test moves it out
    /// of slim with no other edit". That is #453's move 5c re-opened on the
    /// other side of the split.
    /// </para>
    /// <para>
    /// These assert the <em>workflow</em>, not a local target, because a
    /// developer's machine is not the thing that blocks a merge.
    /// </para>
    /// </remarks>
    [Fact]
    public void Slim_carries_a_size_pin_for_each_project()
    {
        var tiers = Tiers();

        foreach (var key in new[] { "AUTONATE_TIER_COUNT_SLIM_BACKEND", "AUTONATE_TIER_COUNT_SLIM_E2E" })
        {
            Assert.True(tiers.ContainsKey(key), $"{key} is missing from tests/tiers.env, so the "
                + "tier that blocks merges can shrink without anything noticing (#476).");

            Assert.True(int.TryParse(tiers[key], out var count) && count > 0,
                $"{key} is '{tiers[key]}'. A pin of zero passes against an empty tier.");
        }
    }

    [Fact]
    public void The_workflow_pins_the_backend_discovery_count()
    {
        var workflow = File.ReadAllText(
            Path.Combine(RepoRoot.Path, ".github", "workflows", "ci.yml"));

        // Reconciliation compares the shards against the SAME discovery run, so
        // a deleted test moves both numbers and reconciles perfectly against a
        // smaller suite. Only a pin can see that.
        Assert.Contains("AUTONATE_TIER_COUNT_SLIM_BACKEND", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void The_workflow_gates_the_slim_e2e_run_on_a_trx()
    {
        var workflow = File.ReadAllText(
            Path.Combine(RepoRoot.Path, ".github", "workflows", "ci.yml"));

        // The console logger leaves nothing to count. Without a trx the e2e job
        // has no floor at all, which is how a trait on a slim class removed a
        // test from the merge gate in silence.
        Assert.Contains("e2e.trx", workflow, StringComparison.Ordinal);
        Assert.Contains("tier_gate.py", workflow, StringComparison.Ordinal);
        Assert.Contains("AUTONATE_TIER_COUNT_SLIM_E2E", workflow, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(RepoRoot.Path, ".github", "scripts", "tier_gate.py")),
            ".github/scripts/tier_gate.py is missing but ci.yml calls it.");
    }

    /// <summary>
    /// Removed in favour of behavioural tests (#485).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to assert <c>text.Contains("notExecuted") || text.Contains("skipped")</c>
    /// over each gate script. Two things were wrong with it. It was satisfied by
    /// a <em>comment</em> — delete both functional lines, keep the prose, and it
    /// stayed green. And the field it pinned was the wrong one:
    /// <c>notExecuted</c> is never populated by VSTest, so the guard was
    /// protecting a reader of an attribute that is always zero.
    /// </para>
    /// <para>
    /// <c>ShardReportScriptTests</c> now drives all three scripts against trx
    /// files captured from a real run with a real <c>[Fact(Skip)]</c>, and
    /// reverting the fix turns exactly three of them red. A test that runs the
    /// script beats any grep of its source, so this is deleted rather than
    /// tightened.
    /// </para>
    /// </remarks>

    /// <summary>
    /// The backend project carries no <c>RequiresService</c> trait (#490).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>TestTierTraitTests</c> validates every trait value against the known
    /// services, but it reflects over its own assembly — the E2E one. A typo'd
    /// trait in the backend project is invisible to it, and the backend is where
    /// somebody who has just read CLAUDE.md's tier section would most plausibly
    /// add one.
    /// </para>
    /// <para>
    /// The backend shards run <b>unfiltered</b>, so a trait here would not move
    /// the test out of slim — it would simply be inert, and the tier documents
    /// would be describing a boundary that does not exist on this side. Both
    /// <c>tests/tiers.env</c> and #476's own reasoning state this as fact; this
    /// keeps it a fact.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_backend_project_carries_no_service_trait()
    {
        var offenders = Directory
            .EnumerateFiles(Path.Combine(RepoRoot.Path, "tests", "AutoNate.Web.Tests"), "*.cs",
                SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !p.EndsWith(nameof(TestTierDefinitionTests) + ".cs", StringComparison.Ordinal))
            .Select(p => (Path: Path.GetRelativePath(RepoRoot.Path, p), Text: File.ReadAllText(p)))
            // NOT the literal `[Trait("RequiresService"` (#490). Written that
            // way this guard missed `[Xunit.Trait("RequiresService", ...)]` --
            // caught by mutating it, which is the only reason I know. Match the
            // attribute call however it is qualified or spaced.
            .Where(f => ServiceTrait.IsMatch(f.Text))
            .Select(f => f.Path)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These backend tests carry a RequiresService trait. The backend shards run "
            + "unfiltered, so the trait does nothing except make the tier documents wrong — "
            + "and TestTierTraitTests cannot see it, because it reflects over the E2E "
            + "assembly (#490):\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Both new regexes can see what they forbid (#492).
    /// </summary>
    /// <remarks>
    /// Without these, a pattern typo'd into matching nothing reports a clean
    /// tree forever — the failure mode CLAUDE.md names for the Semgrep rule
    /// floor, and the one <see cref="The_guard_can_see_a_real_offender"/>
    /// already closes for the tee regex. Every string here is a form that
    /// compiles or a filter that runs.
    /// </remarks>
    [Fact]
    public void The_absence_guards_can_see_what_they_forbid()
    {
        foreach (var filter in new[]
        {
            "--filter \"RequiresService!=Flowable\"",
            "--filter 'RequiresService!=Flowable'",
            "--filter RequiresService!=Flowable",
            "RequiresService != Flowable",
        })
        {
            Assert.Matches(HandWrittenTierFilter, filter);
        }

        // And not on the shared definition, or the guard cannot coexist with
        // the thing it protects.
        Assert.DoesNotMatch(HandWrittenTierFilter, "--filter \"$AUTONATE_TIER_SLIM_FILTER\"");
        Assert.DoesNotMatch(HandWrittenTierFilter, "--filter \"$(AUTONATE_TIER_FULL_LOCAL_FILTER)\"");

        foreach (var trait in new[]
        {
            "[Trait(\"RequiresService\", \"Flowable\")]",
            "[Xunit.Trait(\"RequiresService\", \"Flowable\")]",
            "[ Trait ( \"RequiresService\" , \"Flowable\" )]",
        })
        {
            Assert.Matches(ServiceTrait, trait);
        }

        Assert.DoesNotMatch(ServiceTrait, "[Trait(\"Category\", \"Slow\")]");
    }

    /// <summary>
    /// `make test-slim` checks for skipped tests, as GitHub does (#492).
    /// </summary>
    /// <remarks>
    /// <para>
    /// GitHub fails on any skipped test — <c>tier_gate.py</c> in the e2e job,
    /// <c>reconcile_shards.py</c> in the backend one. This target reproduced
    /// neither, so <c>dotnet test</c>'s exit 0 and a discovery-count pin that a
    /// <c>[Fact(Skip)]</c> does not move left it green while the PR went red.
    /// </para>
    /// <para>
    /// That is the precise failure CLAUDE.md's "runs every test GitHub runs"
    /// promise exists to prevent, so the promise needed the check rather than
    /// another caveat.
    /// </para>
    /// </remarks>
    [Fact]
    public void Slim_checks_for_skipped_tests_the_way_github_does()
    {
        var recipe = Recipe("test-slim");

        var gates = recipe.Split("tier_gate.py").Length - 1;
        Assert.True(gates >= 2,
            $"`make test-slim` invokes tier_gate.py {gates} time(s); it needs one per project. "
            + "Without it a [Fact(Skip)] is green locally and red on the PR (#492).");

        // A trx to read, or the gate has nothing to work from.
        Assert.Contains("trx;LogFileName=slim-backend.trx", recipe, StringComparison.Ordinal);
        Assert.Contains("trx;LogFileName=slim-e2e.trx", recipe, StringComparison.Ordinal);
    }

    /// <summary>
    /// full-local runs the app under a Dapr sidecar (#487).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The trait promised an exercise no tier performed: the fixture started the
    /// app bare with <c>AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR=true</c>, so the one
    /// Dapr-traited spec passed on the no-sidecar path.
    /// </para>
    /// <para>
    /// It is driven by the TIER rather than by the fixture, and that is
    /// load-bearing: slim runs the same 200-odd untraited specs through the same
    /// fixture on a runner with no Dapr at all, so a fixture that always
    /// required a sidecar would take the merge gate down. The variable is the
    /// seam.
    /// </para>
    /// </remarks>
    [Fact]
    public void Full_local_runs_the_app_under_a_dapr_sidecar()
    {
        Assert.Contains("AUTONATE_E2E_DAPR=1", Recipe("test-full-local"), StringComparison.Ordinal);

        // And slim must NOT set it -- GitHub has no Dapr CLI, so a sidecar there
        // would fail every E2E spec rather than the one that needs it.
        Assert.DoesNotContain("AUTONATE_E2E_DAPR", Recipe("test-slim"), StringComparison.Ordinal);

        var workflow = File.ReadAllText(
            Path.Combine(RepoRoot.Path, ".github", "workflows", "ci.yml"));
        Assert.DoesNotContain("AUTONATE_E2E_DAPR", workflow, StringComparison.Ordinal);

        var fixture = File.ReadAllText(Path.Combine(
            RepoRoot.Path, "tests", "AutoNate.E2E.Tests", "AutoNateE2EFixture.cs"));

        // The bypass must be conditional. Unconditional, the app never checks for
        // a sidecar and the whole arrangement is decoration again.
        Assert.Contains("if (underDapr)", fixture, StringComparison.Ordinal);

        // And the app must be pointed at THIS run's sidecar. appsettings hard-codes
        // 127.0.0.1:3500, and on a developer machine a stray `make app-dapr`
        // daprd answers there -- without the override the fixture would start a
        // sidecar the app never talks to.
        Assert.Contains("Dapr__HttpEndpoint", fixture, StringComparison.Ordinal);

        // THE COMPONENTS MUST COME FROM THE TRACKED TREE (#501). This is the
        // real check: the directory has to exist in a fresh clone. The fixture
        // used to point at infra/mounts/dapr-dashboard/components, which
        // `.gitignore` excludes, `infra-prepare` creates by copying and
        // `infra-reset` deletes -- so a clean checkout failed with
        // "error validating resources path" and only `make test-full-local` hid
        // it, because infra-ensure runs first.
        var components = Path.Combine(RepoRoot.Path, "infra", "dapr", "components");

        Assert.True(Directory.Exists(components),
            $"{components} is missing. The E2E sidecar loads its pub/sub component from there, "
            + "and it must be a tracked directory rather than one a make target generates.");

        Assert.True(File.Exists(Path.Combine(components, "pubsub.yaml")),
            "infra/dapr/components/pubsub.yaml is missing; without it the sidecar starts with no "
            + "pub/sub and the Bus Watcher firehose stays empty.");

        // A text match, and named as one: it pins the specific regression rather
        // than the property. The two assertions above are what actually check
        // the property.
        Assert.DoesNotContain("\"mounts\"", fixture, StringComparison.Ordinal);
    }
}
