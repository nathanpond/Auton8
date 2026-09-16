using System.Diagnostics;
using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// Tests for <c>shard_report.py</c> and <c>reconcile_shards.py</c> — the pair
/// that decides whether a sharded CI run is trustworthy.
/// </summary>
/// <remarks>
/// Reconciliation is the guard against sharding's quiet failure: a filter that
/// matches nothing makes the build faster and greener. A guard nobody has
/// watched fail is a guard nobody knows works, so the loss path is exercised
/// here as well as in CI.
/// </remarks>
public sealed class ShardReportScriptTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("shardreport").FullName;

    public void Dispose() => Directory.Delete(_work, recursive: true);

    private static string Script(string name) =>
        Path.Combine(RepoRoot.Path, ".github", "scripts", name);

    private sealed record Result(int ExitCode, string Stdout, string Stderr);

    private static Result Run(string script, params string[] args)
    {
        var psi = new ProcessStartInfo("python3")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(Script(script));
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new(process.ExitCode, stdout, stderr);
    }

    private static string Trx(int total, int passed, int failed, params (string Name, string Message)[] failures)
    {
        var results = string.Concat(failures.Select(f => $"""
              <UnitTestResult testId="{Guid.NewGuid()}" testName="{f.Name}" outcome="Failed">
                <Output><ErrorInfo><Message>{f.Message}</Message></ErrorInfo></Output>
              </UnitTestResult>
            """));

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
            {results}
              </Results>
              <ResultSummary outcome="Completed">
                <Counters total="{total}" passed="{passed}" failed="{failed}" />
              </ResultSummary>
            </TestRun>
            """;
    }

    private string WriteTrx(string content)
    {
        var path = Path.Combine(_work, "shard.trx");
        File.WriteAllText(path, content);
        return path;
    }

    private (string Count, string Summary) Report(string trxPath, string shard = "3", int elapsed = 250)
    {
        var count = Path.Combine(_work, "shard-count.txt");
        var summary = Path.Combine(_work, "summary.md");
        var result = Run("shard_report.py",
            "--trx", trxPath, "--shard", shard, "--elapsed", elapsed.ToString(),
            "--count-file", count, "--summary-file", summary);
        Assert.Equal(0, result.ExitCode);
        return (File.ReadAllText(count), File.ReadAllText(summary));
    }

    [Fact]
    public void The_executed_count_comes_from_the_trx_counters()
    {
        var (count, _) = Report(WriteTrx(Trx(total: 240, passed: 240, failed: 0)));

        Assert.Contains("executed=240", count, StringComparison.Ordinal);
        Assert.Contains("shard=3", count, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_trx_reports_zero_rather_than_crashing()
    {
        // A shard that died before writing results must reconcile as 0, not be
        // skipped — being skipped is how the loss becomes invisible.
        var (count, summary) = Report(Path.Combine(_work, "does-not-exist.trx"));

        Assert.Contains("executed=0", count, StringComparison.Ordinal);
        Assert.Contains("No trx produced", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_red_shard_names_its_failures_and_their_first_assertion()
    {
        // The AC: a red build is diagnosable without opening every shard's log.
        var (_, summary) = Report(WriteTrx(Trx(
            total: 12, passed: 10, failed: 2,
            ("Ns.AlphaTests.Explodes", "Assert.Equal() Failure: Values differ"),
            ("Ns.BetaTests.AlsoExplodes", "Expected 3 but found 4"))));

        Assert.Contains("Ns.AlphaTests.Explodes", summary, StringComparison.Ordinal);
        Assert.Contains("Assert.Equal() Failure: Values differ", summary, StringComparison.Ordinal);
        Assert.Contains("Ns.BetaTests.AlsoExplodes", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pipe_in_an_assertion_message_cannot_break_the_markdown_table()
    {
        var (_, summary) = Report(WriteTrx(Trx(
            total: 1, passed: 0, failed: 1,
            ("Ns.PipeTests.Fails", "Expected a|b but found c|d"))));

        // Escaped, so the row still has exactly the columns it declares.
        Assert.Contains(@"a\|b", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Reconciliation_passes_when_the_counts_add_up()
    {
        WriteCounts(("0", 100), ("1", 150));

        var result = Run("reconcile_shards.py",
            "--counts-dir", _work, "--expected", "250");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("All 250 discovered test cases ran", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Reconciliation_fails_when_a_shard_ran_fewer_tests_than_discovered()
    {
        // The whole point. This is the shape of a filter that matched nothing:
        // the build is faster and everything is green.
        WriteCounts(("0", 100), ("1", 0));

        var result = Run("reconcile_shards.py",
            "--counts-dir", _work, "--expected", "250");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Sharding lost tests: 100 executed, 250 discovered", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Test_loss_is_reported_even_when_a_shard_also_failed()
    {
        // Ordering matters: "these tests never ran" and "these tests failed"
        // demand different responses, and the second must not hide the first.
        WriteCounts(("0", 100));

        var result = Run("reconcile_shards.py",
            "--counts-dir", _work, "--expected", "250", "--shards-result", "failure");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("lost tests", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failing_shard_fails_reconciliation_once_the_counts_agree()
    {
        WriteCounts(("0", 250));

        var result = Run("reconcile_shards.py",
            "--counts-dir", _work, "--expected", "250", "--shards-result", "failure");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("at least one shard reported failures", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void No_counts_at_all_is_a_failure_not_a_pass()
    {
        var result = Run("reconcile_shards.py",
            "--counts-dir", _work, "--expected", "250");

        Assert.Equal(1, result.ExitCode);
    }


    // ---- the skip gate, driven by REAL runner output (#485) -----------------
    //
    // These use trx files captured from an actual `dotnet test` run of a
    // three-test project, one of them `[Fact(Skip = "probe")]`. That is the
    // whole point. #476 "fixed" the skip gate against a HAND-WRITTEN trx
    // carrying notExecuted="1" -- a shape VSTest does not emit -- so the gate
    // shipped reading an attribute that is always zero, and every piece of
    // evidence for it was green. A fixture the runner did not produce cannot
    // certify a reader of what the runner produces.

    private static string Fixture(string name) => Path.Combine(
        RepoRoot.Path, "tests", "AutoNate.Web.Tests", "Infrastructure", "TrxFixtures", name);

    /// <summary>
    /// The captured fixture really is the shape the runner emits (#485).
    /// </summary>
    /// <remarks>
    /// If someone regenerates these files by hand, this fails first and says
    /// why — <c>notExecuted</c> is <b>zero</b> on a run that skipped a test,
    /// and the skip shows only as the total-executed gap.
    /// </remarks>
    [Fact]
    public void The_captured_trx_has_the_shape_the_runner_actually_emits()
    {
        var skip = File.ReadAllText(Fixture("real-skip.trx"));

        Assert.Contains("notExecuted=\"0\"", skip, StringComparison.Ordinal);
        Assert.Contains("total=\"3\"", skip, StringComparison.Ordinal);
        Assert.Contains("executed=\"2\"", skip, StringComparison.Ordinal);
        Assert.Contains("outcome=\"NotExecuted\"", skip, StringComparison.Ordinal);
    }

    [Fact]
    public void A_real_skipped_test_is_reported_by_shard_report()
    {
        var counts = Path.Combine(_work, "shard-count.txt");

        var result = Run("shard_report.py",
            "--trx", Fixture("real-skip.trx"), "--shard", "1", "--elapsed", "10",
            "--count-file", counts, "--summary-file", Path.Combine(_work, "s.md"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("skipped 1", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("skipped=1", File.ReadAllText(counts), StringComparison.Ordinal);
    }

    [Fact]
    public void A_real_clean_run_reports_no_skips()
    {
        // The complement. A skip check that always fires is not a check, and
        // "always fires" is the cheapest way to make one look strict.
        var counts = Path.Combine(_work, "shard-count.txt");

        var result = Run("shard_report.py",
            "--trx", Fixture("real-clean.trx"), "--shard", "1", "--elapsed", "10",
            "--count-file", counts, "--summary-file", Path.Combine(_work, "s.md"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("skipped 0", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("skipped=0", File.ReadAllText(counts), StringComparison.Ordinal);
    }

    /// <summary>
    /// A skip fails reconciliation even though the counts reconcile (#485).
    /// </summary>
    /// <remarks>
    /// This is the shape of the original defect: the sum matches discovery
    /// exactly, because a trx counts a skipped test in <c>total</c>. Only the
    /// separate skip check can see it.
    /// </remarks>
    [Fact]
    public void A_real_skip_fails_reconciliation_while_the_counts_still_add_up()
    {
        var dir = Path.Combine(_work, "shard-count-1");
        Directory.CreateDirectory(dir);

        Run("shard_report.py",
            "--trx", Fixture("real-skip.trx"), "--shard", "1", "--elapsed", "10",
            "--count-file", Path.Combine(dir, "shard-count.txt"),
            "--summary-file", Path.Combine(_work, "s.md"));

        var result = Run("reconcile_shards.py",
            "--counts-dir", _work, "--expected", "3", "--shards-result", "success");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("skipped", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_real_clean_run_passes_reconciliation()
    {
        var dir = Path.Combine(_work, "shard-count-1");
        Directory.CreateDirectory(dir);

        Run("shard_report.py",
            "--trx", Fixture("real-clean.trx"), "--shard", "1", "--elapsed", "10",
            "--count-file", Path.Combine(dir, "shard-count.txt"),
            "--summary-file", Path.Combine(_work, "s.md"));

        var result = Run("reconcile_shards.py",
            "--counts-dir", _work, "--expected", "3", "--shards-result", "success");

        Assert.Equal(0, result.ExitCode);
    }

    // ---- tier_gate.py, which had no behavioural test at all (#485, #490) ----

    [Theory]
    [InlineData("real-skip.trx", 3, 1, "skipped")]
    [InlineData("real-clean.trx", 2, 1, "pinned at 2")]
    [InlineData("real-clean.trx", 4, 1, "pinned at 4")]
    public void The_tier_gate_fails_on_a_skip_or_a_moved_count(
        string fixture, int expected, int exitCode, string says)
    {
        var result = Run("tier_gate.py",
            "--trx", Fixture(fixture), "--expected", expected.ToString(), "--label", "probe");

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Contains(says, result.Stdout + result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_tier_gate_passes_a_real_clean_run_at_its_pin()
    {
        var result = Run("tier_gate.py",
            "--trx", Fixture("real-clean.trx"), "--expected", "3", "--label", "probe");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("none skipped", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tier_gate_fails_closed_on_a_missing_trx()
    {
        var result = Run("tier_gate.py",
            "--trx", Path.Combine(_work, "never-written.trx"), "--expected", "3", "--label", "probe");

        Assert.Equal(1, result.ExitCode);
    }

    private void WriteCounts(params (string Shard, int Executed)[] shards)
    {
        foreach (var (shard, executed) in shards)
        {
            var dir = Path.Combine(_work, $"shard-count-{shard}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "shard-count.txt"),
                $"shard={shard}\nexecuted={executed}\n");
        }
    }
}
