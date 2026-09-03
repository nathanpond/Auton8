using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// Shape assertions on the sharded backend jobs in ci.yml.
/// </summary>
/// <remarks>
/// Sharding is proven by running it. What these pin are the four properties
/// whose loss would be silent — each one, if removed, turns a broken run into a
/// green one rather than a red one. That is the whole reason they are worth a
/// test: nothing else in CI would report their absence.
/// </remarks>
public sealed class BackendShardingWorkflowTests
{
    private static string Workflow() =>
        File.ReadAllText(Path.Combine(RepoRoot.Path, ".github", "workflows", "ci.yml"));

    private static string Job(string name)
    {
        var workflow = Workflow();
        var start = workflow.IndexOf($"\n  {name}:", StringComparison.Ordinal);
        Assert.True(start >= 0, $"ci.yml has no '{name}' job.");

        // The next top-level job key, i.e. the next line indented by exactly
        // two spaces that is not a comment or a nested mapping.
        var rest = workflow[(start + 1)..];
        var lines = rest.Split('\n');
        var length = lines[0].Length + 1;
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var isTopLevelKey =
                line.Length > 2
                && line[0] == ' ' && line[1] == ' ' && line[2] != ' ' && line[2] != '#'
                && line.TrimEnd().EndsWith(':');
            if (isTopLevelKey) break;
            length += line.Length + 1;
        }
        return rest[..Math.Min(length, rest.Length)];
    }

    [Fact]
    public void A_failing_shard_does_not_cancel_the_others()
    {
        // Cancelling siblings hides whether other shards also failed, and — the
        // real damage — starves reconciliation of the counts it needs to tell a
        // lost test from a failing one.
        Assert.Contains("fail-fast: false", Job("backend"), StringComparison.Ordinal);
    }

    [Fact]
    public void Reconciliation_runs_even_when_shards_fail()
    {
        var reconcile = Job("backend-reconcile");

        // Without always(), a failing shard skips this job, masking a lost test
        // behind the very failure it caused.
        Assert.Contains("if: always()", reconcile, StringComparison.Ordinal);
        Assert.Contains("needs: [backend-discover, backend]", reconcile, StringComparison.Ordinal);
        Assert.Contains("reconcile_shards.py", reconcile, StringComparison.Ordinal);
    }

    [Fact]
    public void Reconciliation_compares_against_the_discovered_total()
    {
        // The comparison is what makes it a guard rather than a report. If the
        // expected number stopped coming from discovery, the check would pass
        // trivially.
        Assert.Contains(
            "--expected \"${{ needs.backend-discover.outputs.total }}\"",
            Job("backend-reconcile"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_red_shard_still_publishes_its_count()
    {
        var backend = Job("backend");

        // The count must survive a failing shard, or reconciliation cannot
        // distinguish "these tests failed" from "these tests never ran".
        Assert.Contains("if: always()", backend, StringComparison.Ordinal);
        Assert.Contains("shard_report.py", backend, StringComparison.Ordinal);

        // And the shard must still go red afterwards — the test step swallows
        // its own exit code so the count can be published first.
        Assert.Contains("steps.test.outputs.status != '0'", backend, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_shard_gets_its_own_postgres_and_nats()
    {
        var backend = Job("backend");

        // The suite touches cluster-wide objects (plg_readers), so shards must
        // not share a server. Each matrix leg is its own VM, which makes these
        // per-shard by construction — but only while they are declared inside
        // the matrix job rather than hoisted somewhere shared.
        Assert.Contains("autonate-ci-postgres", backend, StringComparison.Ordinal);
        Assert.Contains("autonate-ci-nats", backend, StringComparison.Ordinal);

        // Both carry a command, which is why neither can be a service
        // container: `options:` goes to `docker create`, which has no --cmd.
        Assert.Contains("max_connections=300", backend, StringComparison.Ordinal);
        Assert.Contains("--jetstream", backend, StringComparison.Ordinal);
    }

    [Fact]
    public void Discovery_happens_once_and_feeds_the_matrix()
    {
        var discover = Job("backend-discover");
        var backend = Job("backend");

        Assert.Contains("--list-tests", discover, StringComparison.Ordinal);
        Assert.Contains("partition_tests.py", discover, StringComparison.Ordinal);

        // The matrix comes from discovery's output, so a new test class is
        // picked up with no workflow edit.
        Assert.Contains(
            "shard: ${{ fromJSON(needs.backend-discover.outputs.matrix) }}",
            backend,
            StringComparison.Ordinal);

        // And the shards must not re-discover: a second discovery is a second
        // place for the partitions to disagree.
        Assert.DoesNotContain("--list-tests", backend);
    }

    [Fact]
    public void The_shards_do_not_rebuild_what_discovery_already_built()
    {
        var backend = Job("backend");

        // The expensive half is skipped: no compilation in a shard.
        Assert.Contains("--no-build", backend, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet build", backend);

        // Restore, however, is required even with --no-build, and removing it
        // is silent. obj/*.nuget.g.props imports each package's build assets
        // from ~/.nuget/packages under Condition="Exists(...)", so an
        // unrestored shard skips xunit.runner.visualstudio.props — the VSTest
        // adapter — and `dotnet test` exits 0 having found nothing to run.
        // Eight shards did precisely that.
        Assert.Contains("dotnet restore --locked-mode", backend, StringComparison.Ordinal);
    }

    [Fact]
    public void The_packed_build_covers_every_project_not_just_the_test_one()
    {
        var discover = Job("backend-discover");

        // The regression this exists for. The first sharded run packed only
        // `tests/AutoNate.Web.Tests/bin`, reasoning that its output already
        // contains copies of every DLL it references. True of the DLLs, false
        // of what `dotnet test` needs: with the referenced projects' own bin
        // directories absent, `dotnet test --no-build` on SDK 10.0.400
        // resolved no test source and exited 0 in total silence — no output,
        // no trx, no error. Eight green shards that ran nothing.
        //
        // So the pack list is derived from the projects, and it must stay
        // derived: a hand-written path list is how the next project silently
        // goes missing.
        Assert.Contains("git ls-files '*.csproj'", discover, StringComparison.Ordinal);
        Assert.Contains("pack-list.txt", discover, StringComparison.Ordinal);
        Assert.Contains("tar -czf backend-build.tgz -T pack-list.txt", discover, StringComparison.Ordinal);

        // And it must include obj as well as bin — `--no-build` needs
        // project.assets.json to evaluate the project at all.
        Assert.Contains("for dir in bin obj", discover, StringComparison.Ordinal);

        // An empty pack list is refused rather than shipped: the shards would
        // receive nothing and fail in the silent way described above.
        Assert.Contains("The shards would receive nothing", discover, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shard_that_produces_no_trx_fails_loudly()
    {
        var backend = Job("backend");

        // The step deliberately swallows dotnet test's exit code so the count
        // can be published on a red shard. That makes "exited 0 having run
        // nothing" indistinguishable from a pass unless something checks for
        // the artefact of an actual run.
        Assert.Contains("if [ ! -f trx/shard.trx ]", backend, StringComparison.Ordinal);
        Assert.Contains("No tests ran", backend, StringComparison.Ordinal);

        // And the exit code reaches the log, not only the step output — it was
        // invisible on the first run, which cost a diagnosis cycle.
        Assert.Contains("dotnet test exited", backend, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shard_count_lives_in_exactly_one_place()
    {
        // Retuning after reading the timings should be a one-line change.
        var workflow = Workflow();
        var occurrences = workflow.Split("BACKEND_SHARDS").Length - 1;

        // Once as the declaration, and the uses inside the run block.
        Assert.Contains("BACKEND_SHARDS: 8", workflow, StringComparison.Ordinal);
        Assert.True(occurrences >= 2, "BACKEND_SHARDS is declared but never used.");
        Assert.DoesNotContain("--shards 8", workflow);
    }

    [Fact]
    public void The_e2e_and_spa_jobs_were_not_touched_by_sharding()
    {
        // #67 is explicitly scoped to the backend suite, and the E2E job's
        // Flowable/Dapr trait exclusions are a recorded decision, not an
        // oversight.
        var e2e = Job("e2e");

        Assert.DoesNotContain("matrix", e2e);
        Assert.DoesNotContain("shard", e2e);
        Assert.Contains("RequiresService!=Flowable&RequiresService!=Dapr", Workflow(), StringComparison.Ordinal);
    }
}
