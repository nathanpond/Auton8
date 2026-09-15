using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// Exercises <c>infra/tier-integrity.sh</c>'s skip check against synthetic logs (#453).
/// </summary>
/// <remarks>
/// <para>
/// The script is what stops the full tier reporting success while running less
/// than it is. Its skip half needs no services and no <c>dotnet</c>, so
/// <c>--skips-only</c> exists and these tests run in the slim tier — on GitHub,
/// on every push. A guard that only runs where the thing it guards runs is a
/// guard nobody executes.
/// </para>
/// <para>
/// Both directions are asserted deliberately. A check that fires on everything
/// is as useless as one that fires on nothing, and "always fail" is the cheapest
/// way to make a skip check look strict — so the clean-log case is a test, not
/// an assumption.
/// </para>
/// <para>
/// What this does NOT cover: the count pins, which need a real discovery run,
/// and an observer gutted to a tautology inside a test that still runs (#463).
/// </para>
/// </remarks>
public sealed class TierIntegrityScriptTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("tier-integrity-tests").FullName;

    private static string ScriptPath => Path.Combine(RepoRoot.Path, "infra", "tier-integrity.sh");

    // No /bin/bash on Windows, and the local stack is documented as Docker
    // Desktop on macOS/Linux, so there is nothing to assert there. An early
    // return rather than a Skip attribute, matching PreflightScriptTests -- and
    // because a skipped test is exactly what the script under test counts.
    private static bool ShellAvailable => !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private const string PassingSummary =
        "Passed!  - Failed:     0, Passed:  2653, Skipped:     0, Total:  2653, Duration: 26 m 48 s";

    private string WriteLog(string name, string body)
    {
        var path = Path.Combine(_work, name);
        File.WriteAllText(path, body);
        return path;
    }

    private static (int ExitCode, string Output) RunSkipCheck(params string[] logs)
    {
        var psi = new ProcessStartInfo("/bin/bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoRoot.Path,
        };

        psi.ArgumentList.Add(ScriptPath);
        psi.ArgumentList.Add("--skips-only");
        foreach (var log in logs) psi.ArgumentList.Add(log);

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output);
    }

    [Fact]
    public void A_clean_run_passes()
    {
        if (!ShellAvailable) return;

        var (code, output) = RunSkipCheck(WriteLog("clean.log", PassingSummary));

        Assert.True(code == 0, $"A clean run must not trip the skip check, or the check is just "
            + $"an unconditional failure wearing a costume. Output:\n{output}");
        Assert.Contains("0 skipped", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The exact defect: one attribute argument, and the oracle runs zero cells.
    /// </summary>
    [Fact]
    public void A_skipped_test_fails_the_tier()
    {
        if (!ShellAvailable) return;

        // This is the real line the pre-fix run produced, with the oracle
        // carrying [Theory(Skip = "probe")]: 343 rather than 371, 1 skipped,
        // and `make test-full-local` exiting 0.
        var log = WriteLog("skipped.log",
            "  Skipped AutoNate.E2E.Tests.ExecutionEvidenceExecutionTests.The_element_runs_and_has_its_declared_effect [1 ms]\n"
            + "Failed!  - Failed:     2, Passed:   340, Skipped:     1, Total:   343, Duration: 10 m 26 s\n");

        var (code, output) = RunSkipCheck(log);

        Assert.True(code != 0, $"A skipped test must turn the full tier red. Output:\n{output}");
        Assert.Contains("1 skipped test(s)", output, StringComparison.Ordinal);

        // Naming the test, not just the count -- otherwise the developer's next
        // move is to re-run the whole tier to find out which one.
        Assert.Contains("The_element_runs_and_has_its_declared_effect", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A run that never finished is not a run with zero skips.
    /// </summary>
    /// <remarks>
    /// Without this, a crashed or killed suite writes a log with no summary
    /// line, a grep for a non-zero skip count finds nothing, and the check
    /// passes — reporting a clean bill of health against no evidence at all.
    /// </remarks>
    [Fact]
    public void A_run_with_no_summary_line_fails()
    {
        if (!ShellAvailable) return;

        var log = WriteLog("crashed.log",
            "Determining projects to restore...\nSegmentation fault\n");

        var (code, output) = RunSkipCheck(log);

        Assert.True(code != 0, $"A log with no summary line must fail. Output:\n{output}");
        Assert.Contains("did not finish", output, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_or_missing_log_fails()
    {
        if (!ShellAvailable) return;

        var (emptyCode, emptyOutput) = RunSkipCheck(WriteLog("empty.log", ""));
        Assert.True(emptyCode != 0, $"An empty log must fail. Output:\n{emptyOutput}");

        var (missingCode, missingOutput) = RunSkipCheck(Path.Combine(_work, "never-written.log"));
        Assert.True(missingCode != 0, $"A missing log must fail. Output:\n{missingOutput}");
    }

    /// <summary>
    /// Called with nothing to check, it fails rather than reporting success.
    /// </summary>
    [Fact]
    public void No_logs_at_all_fails()
    {
        if (!ShellAvailable) return;

        var (code, output) = RunSkipCheck();

        Assert.True(code != 0, $"Checking nothing is not the same as finding nothing wrong. Output:\n{output}");
    }

    /// <summary>
    /// Every log is checked, not just the first.
    /// </summary>
    [Fact]
    public void A_skip_in_the_second_log_is_still_caught()
    {
        if (!ShellAvailable) return;

        var (code, output) = RunSkipCheck(
            WriteLog("first.log", PassingSummary),
            WriteLog("second.log",
                "Failed!  - Failed:     0, Passed:   340, Skipped:     3, Total:   343\n"));

        Assert.True(code != 0, $"The E2E log is the second argument, and it is the one that "
            + $"carries the oracle. Output:\n{output}");
        Assert.Contains("3 skipped test(s)", output, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try { Directory.Delete(_work, recursive: true); } catch { /* best effort */ }
    }
}
