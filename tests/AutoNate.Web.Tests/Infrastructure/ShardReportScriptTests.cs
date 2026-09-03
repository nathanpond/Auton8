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
