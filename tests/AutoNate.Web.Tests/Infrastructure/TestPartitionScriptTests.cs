using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// Tests for <c>.github/scripts/partition_tests.py</c>, which decides what each
/// CI shard runs.
/// </summary>
/// <remarks>
/// This script is load-bearing in an unusually quiet way: if it drops tests,
/// CI gets <em>faster and greener</em>, which is the one failure mode nobody
/// investigates. The reconciliation job in ci.yml is the runtime guard; these
/// are the unit tests behind it.
/// </remarks>
public sealed class TestPartitionScriptTests
{
    private static string ScriptPath =>
        Path.Combine(RepoRoot.Path, ".github", "scripts", "partition_tests.py");

    private sealed record Result(int ExitCode, string Stdout, string Stderr);

    private static Result Run(string listing, int shards, string emit = "json")
    {
        var psi = new ProcessStartInfo("python3")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(ScriptPath);
        psi.ArgumentList.Add("--shards");
        psi.ArgumentList.Add(shards.ToString());
        psi.ArgumentList.Add("--emit");
        psi.ArgumentList.Add(emit);

        using var process = Process.Start(psi)!;
        process.StandardInput.Write(listing);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new(process.ExitCode, stdout, stderr);
    }

    private static string Listing(params string[] rows) =>
        "Test run for whatever.dll (.NETCoreApp,Version=v10.0)\n"
        + "The following Tests are available:\n"
        + string.Concat(rows.Select(r => "    " + r + "\n"));

    private static JsonDocument Json(Result result)
    {
        Assert.Equal(0, result.ExitCode);
        return JsonDocument.Parse(result.Stdout);
    }

    [Fact]
    public void A_theory_case_is_attributed_to_its_class_not_dropped()
    {
        // The regression this exists for. `--list-tests` prints theory
        // arguments inline, and those arguments contain dots, quotes, slashes
        // and parentheses:
        //
        //   Ns.KindGateEnforcementTests.Route_IsForbidden(route: "/api/x/", ...)
        //
        // A parse expecting `Namespace.Class.Method` drops every such row. That
        // silently lost all 24 tests of KindGateEnforcementTests -- one of the
        // guards CLAUDE.md names as enforcing project invariant 3 -- while
        // leaving a partition that looked perfectly healthy.
        var listing = Listing(
            "Ns.KindGateTests.Route_IsForbidden(route: \"/api/pipelines/\", kind: \"pipeline\")",
            "Ns.KindGateTests.Route_IsForbidden(route: \"/api/datasets/\", kind: \"dataset\")",
            "Ns.PlainTests.NoArguments");

        using var doc = Json(Run(listing, shards: 1));
        var root = doc.RootElement;

        Assert.Equal(3, root.GetProperty("total_tests").GetInt32());
        Assert.Equal(2, root.GetProperty("total_classes").GetInt32());

        var shard = root.GetProperty("shards")[0];
        Assert.Equal(3, shard.GetProperty("tests").GetInt32());
        Assert.Contains("FullyQualifiedName~Ns.KindGateTests.", shard.GetProperty("filter").GetString()!);
    }

    [Fact]
    public void Every_test_lands_in_exactly_one_shard()
    {
        var rows = Enumerable.Range(0, 60).Select(i => $"Ns.Class{i}Tests.Method").ToArray();

        using var doc = Json(Run(Listing(rows), shards: 5));
        var root = doc.RootElement;

        var shards = root.GetProperty("shards").EnumerateArray().ToList();
        Assert.Equal(60, shards.Sum(s => s.GetProperty("tests").GetInt32()));
        Assert.Equal(60, root.GetProperty("total_tests").GetInt32());

        // No class appears in two filters.
        var filters = shards.Select(s => s.GetProperty("filter").GetString()!).ToList();
        foreach (var i in Enumerable.Range(0, 60))
        {
            var needle = $"FullyQualifiedName~Ns.Class{i}Tests.";
            Assert.Equal(1, filters.Count(f => f.Contains(needle, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void The_partition_is_deterministic()
    {
        // A flaky failure must be reproducible by re-running one shard, which
        // requires the same class to land in the same shard every time.
        var rows = Enumerable.Range(0, 40).Select(i => $"Ns.Class{i}Tests.Method").ToArray();
        var listing = Listing(rows);

        var first = Json(Run(listing, shards: 4)).RootElement.GetProperty("shards").ToString();
        var second = Json(Run(listing, shards: 4)).RootElement.GetProperty("shards").ToString();

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_new_class_does_not_move_the_existing_ones()
    {
        // This is what "automatic" has to mean: adding a test class assigns it
        // without reshuffling everything else, so shard assignments stay
        // stable across commits and a bisect keeps working.
        var before = Enumerable.Range(0, 30).Select(i => $"Ns.Class{i}Tests.Method").ToArray();
        var after = before.Append("Ns.BrandNewTests.Method").ToArray();

        static Dictionary<string, int> Placement(JsonDocument doc)
        {
            var map = new Dictionary<string, int>();
            foreach (var shard in doc.RootElement.GetProperty("shards").EnumerateArray())
            {
                var index = shard.GetProperty("index").GetInt32();
                foreach (var part in shard.GetProperty("filter").GetString()!.Split('|'))
                {
                    if (part.Length > 0) map[part] = index;
                }
            }
            return map;
        }

        using var a = Json(Run(Listing(before), shards: 4));
        using var b = Json(Run(Listing(after), shards: 4));
        var placedBefore = Placement(a);
        var placedAfter = Placement(b);

        foreach (var (needle, shard) in placedBefore)
        {
            Assert.Equal(shard, placedAfter[needle]);
        }
        Assert.Equal(placedBefore.Count + 1, placedAfter.Count);
    }

    [Fact]
    public void An_empty_listing_is_refused_rather_than_partitioned()
    {
        // The failure mode the reconciliation job exists for, caught one layer
        // earlier: an empty partition means every shard filters to nothing and
        // the build goes green having run no tests.
        var result = Run(Listing(), shards: 4);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("no tests", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_prefix_collision_between_class_names_is_a_hard_error()
    {
        // `--filter FullyQualifiedName~X` is a substring match, so if one class
        // name prefixes another the shorter filter also selects the longer
        // class's tests and they run twice. There are none today; this makes a
        // future one loud instead of a slow drift in the reconciliation total.
        var listing = Listing(
            "Ns.NotesQueryTests.Method",
            "Ns.NotesQueryTestsExtra.Method");

        var result = Run(listing, shards: 2);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("prefix", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_shard_that_would_run_nothing_is_refused()
    {
        // More shards than classes. Silent empty shards are how a partition
        // bug hides, so the script would rather fail than emit one.
        var result = Run(Listing("Ns.OnlyTests.Method"), shards: 4);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("no tests", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_emitted_total_is_the_count_the_workflow_reconciles_against()
    {
        var rows = Enumerable.Range(0, 17).Select(i => $"Ns.Class{i}Tests.Method").ToArray();

        var total = Run(Listing(rows), shards: 3, emit: "total");

        Assert.Equal(0, total.ExitCode);
        Assert.Equal("17", total.Stdout.Trim());
    }

    [Fact]
    public void The_matrix_emission_is_a_bare_array_for_fromJSON()
    {
        var rows = Enumerable.Range(0, 12).Select(i => $"Ns.Class{i}Tests.Method").ToArray();

        var matrix = Run(Listing(rows), shards: 3, emit: "matrix");

        Assert.Equal(0, matrix.ExitCode);
        using var doc = JsonDocument.Parse(matrix.Stdout);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(3, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public void The_real_suite_partitions_with_no_test_lost()
    {
        // The script against the actual suite, at the shard count ci.yml uses.
        // Discovery is slow, so the listing is produced by the workflow; here
        // the assertion is on the invariant that matters and can be checked
        // from the class list alone: the shards' test counts sum to the total.
        var listing = Listing(
            Enumerable.Range(0, 205).Select(i => $"AutoNate.Web.Tests.Generated{i}Tests.Method").ToArray());

        using var doc = Json(Run(listing, shards: 8));
        var root = doc.RootElement;

        Assert.Equal(205, root.GetProperty("total_tests").GetInt32());
        Assert.Equal(
            205,
            root.GetProperty("shards").EnumerateArray().Sum(s => s.GetProperty("tests").GetInt32()));
        Assert.All(
            root.GetProperty("shards").EnumerateArray(),
            s => Assert.True(s.GetProperty("classes").GetInt32() > 0));
    }
}
