using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// Exercises <c>infra/preflight.sh</c> against stubbed tools.
/// </summary>
/// <remarks>
/// Hermetic on purpose: every check runs against stub executables on a
/// constructed PATH, never against whatever the developer or the CI runner
/// happens to have installed. A test that passes because the machine already
/// has Node would say nothing about the script.
/// </remarks>
public sealed class PreflightScriptTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("preflight-tests").FullName;

    private string ScriptPath => Path.Combine(RepoRoot.Path, "infra", "preflight.sh");

    // The script is POSIX sh and shells out to sed/awk/sort/mktemp. Windows has
    // no /bin/sh, and the local stack is documented as Docker Desktop on
    // macOS/Linux, so there is nothing to assert there.
    private static bool ShellAvailable => !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private string StubDir
    {
        get
        {
            var dir = Path.Combine(_work, "bin");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private void Stub(string name, string versionOutput)
    {
        var path = Path.Combine(StubDir, name);
        File.WriteAllText(path, $"#!/bin/sh\necho '{versionOutput}'\n");
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private string WritePrerequisites(params string[] lines)
    {
        var path = Path.Combine(_work, "prerequisites");
        File.WriteAllLines(path, lines);
        return path;
    }

    private string WriteCompose(string body)
    {
        var path = Path.Combine(_work, "compose.yml");
        File.WriteAllText(path, body);
        return path;
    }

    private (int ExitCode, string Output) Run(string prerequisites, string compose)
    {
        var psi = new ProcessStartInfo("/bin/sh", $"\"{ScriptPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // Stub dir first, then only the system directories the script's own
        // helpers live in. /usr/local/bin and Homebrew are deliberately absent
        // so a real docker or dapr cannot satisfy a "not found" case.
        psi.Environment["PATH"] = $"{StubDir}:/usr/bin:/bin";
        psi.Environment["AUTONATE_PREREQ_FILE"] = prerequisites;
        psi.Environment["AUTONATE_COMPOSE_FILE"] = compose;

        using var process = Process.Start(psi)!;
        var output = new StringBuilder();
        output.Append(process.StandardOutput.ReadToEnd());
        output.Append(process.StandardError.ReadToEnd());
        process.WaitForExit();

        return (process.ExitCode, output.ToString());
    }

    private const string OnePortCompose =
        """
        services:
          svc:
            image: example:1
            ports:
              - "127.0.0.1:47971:47971"
        """;

    // ── Success ─────────────────────────────────────────────────────────────

    [Fact]
    public void All_prerequisites_present_and_current_exits_zero()
    {
        if (!ShellAvailable) return;

        Stub("faketool", "faketool version 3.2.1");
        Stub("docker", "Docker version 25.0.3, build 4debf41");

        var prereq = WritePrerequisites(
            "faketool|faketool|--version|3.0|install faketool",
            "docker|docker|--version|24.0|install docker");

        var (exit, output) = Run(prereq, WriteCompose(OnePortCompose));

        Assert.Equal(0, exit);
        Assert.Contains("All checks passed", output, StringComparison.Ordinal);
        Assert.Contains("3.2.1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Version_is_read_from_the_first_dotted_number_not_the_last()
    {
        if (!ShellAvailable) return;

        // Every tool this script checks would be mis-read by greedy matching:
        // a build hash follows Docker's version, Compose appends "-desktop.1",
        // and dapr prints a second version on a second line. Each of those
        // shapes is represented here.
        Stub("dockerish", "Docker version 25.0.3, build 4debf41");
        Stub("composeish", "Docker Compose version v2.24.5-desktop.1");
        Stub("daprish", "CLI version: 1.17.1\nRuntime version: 1.17.5");

        var prereq = WritePrerequisites(
            "dockerish|dockerish|--version|24.0|x",
            "composeish|composeish|--version|2.20|x",
            "daprish|daprish|--version|1.14|x");

        var (exit, output) = Run(prereq, WriteCompose(OnePortCompose));

        Assert.Equal(0, exit);
        Assert.Contains("25.0.3", output, StringComparison.Ordinal);
        Assert.Contains("2.24.5", output, StringComparison.Ordinal);
        Assert.Contains("1.17.1", output, StringComparison.Ordinal);
    }

    // ── Missing and outdated tools ──────────────────────────────────────────

    [Fact]
    public void Missing_tool_names_itself_the_required_version_and_how_to_install_it()
    {
        if (!ShellAvailable) return;

        var prereq = WritePrerequisites(
            "dapr|dapr|--version|1.14|brew install dapr/tap/dapr-cli");

        var (exit, output) = Run(prereq, WriteCompose(OnePortCompose));

        Assert.NotEqual(0, exit);
        Assert.Contains("dapr", output, StringComparison.Ordinal);
        Assert.Contains("not found", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.14", output, StringComparison.Ordinal);
        Assert.Contains("brew install dapr/tap/dapr-cli", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Outdated_tool_reports_both_the_found_and_the_required_version()
    {
        if (!ShellAvailable) return;

        Stub("node", "v18.19.0");

        var prereq = WritePrerequisites("node|node|--version|24.0|nvm install 24");

        var (exit, output) = Run(prereq, WriteCompose(OnePortCompose));

        Assert.NotEqual(0, exit);
        Assert.Contains("18.19.0", output, StringComparison.Ordinal);
        Assert.Contains("24.0", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_problem_is_reported_in_one_pass()
    {
        if (!ShellAvailable) return;

        // The whole point of the script: a contributor fixes their machine in
        // one pass, not one pass per missing tool. A short-circuiting
        // implementation passes every other test in this class.
        Stub("present", "present version 9.9.9");

        var prereq = WritePrerequisites(
            "alpha|alpha|--version|1.0|install alpha",
            "present|present|--version|1.0|install present",
            "beta|beta|--version|2.0|install beta",
            "gamma|gamma|--version|3.0|install gamma");

        var (exit, output) = Run(prereq, WriteCompose(OnePortCompose));

        Assert.NotEqual(0, exit);
        Assert.Contains("alpha", output, StringComparison.Ordinal);
        Assert.Contains("beta", output, StringComparison.Ordinal);
        Assert.Contains("gamma", output, StringComparison.Ordinal);
        Assert.Contains("3 problem(s) found", output, StringComparison.Ordinal);
    }

    // ── Ports ───────────────────────────────────────────────────────────────

    [Fact]
    public void Occupied_port_is_reported_and_points_at_the_override_file()
    {
        if (!ShellAvailable) return;

        Stub("present", "present version 9.9.9");
        var prereq = WritePrerequisites("present|present|--version|1.0|x");

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var compose = WriteCompose(
            $"""
             services:
               svc:
                 image: example:1
                 ports:
                   - "127.0.0.1:{port}:{port}"
             """);

        var (exit, output) = Run(prereq, compose);

        Assert.NotEqual(0, exit);
        Assert.Contains(port.ToString(), output, StringComparison.Ordinal);
        Assert.Contains("docker-compose.override.yml", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Ports_are_derived_from_the_compose_file_not_hard_coded()
    {
        if (!ShellAvailable) return;

        // A service the script has never heard of must still be checked, which
        // is what stops the list going stale the moment a service is added.
        Stub("present", "present version 9.9.9");
        var prereq = WritePrerequisites("present|present|--version|1.0|x");

        var compose = WriteCompose(
            """
            services:
              a-brand-new-service:
                image: example:1
                ports:
                  - "127.0.0.1:47972:47972"
            """);

        var (_, output) = Run(prereq, compose);

        Assert.Contains("a-brand-new-service", output, StringComparison.Ordinal);
        Assert.Contains("47972", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Interpolated_port_default_is_used_rather_than_the_literal_expression()
    {
        if (!ShellAvailable) return;

        Stub("present", "present version 9.9.9");
        var prereq = WritePrerequisites("present|present|--version|1.0|x");

        var compose = WriteCompose(
            """
            services:
              svc:
                image: example:1
                ports:
                  - "127.0.0.1:${SOME_PORT:-47973}:47973"
            """);

        var (_, output) = Run(prereq, compose);

        Assert.Contains("47973", output, StringComparison.Ordinal);
        Assert.DoesNotContain("SOME_PORT", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Profile_gated_service_ports_are_skipped_unless_the_profile_is_requested()
    {
        if (!ShellAvailable) return;

        // Otherwise `make infra-up` would demand a free port for a container it
        // is not going to start — which after M2's Keycloak and M0's app
        // profile would be several.
        Stub("present", "present version 9.9.9");
        var prereq = WritePrerequisites("present|present|--version|1.0|x");

        var compose = WriteCompose(
            """
            services:
              always:
                image: example:1
                ports:
                  - "127.0.0.1:47974:47974"
              gated:
                image: example:1
                profiles:
                  - extras
                ports:
                  - "127.0.0.1:47975:47975"
            """);

        var (_, output) = Run(prereq, compose);

        Assert.Contains("47974", output, StringComparison.Ordinal);
        Assert.DoesNotContain("47975", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_compose_file_with_no_ports_fails_rather_than_passing_vacuously()
    {
        if (!ShellAvailable) return;

        // A port check that silently finds nothing reports a clean machine,
        // which is worse than not running it at all.
        Stub("present", "present version 9.9.9");
        var prereq = WritePrerequisites("present|present|--version|1.0|x");

        var compose = WriteCompose(
            """
            services:
              svc:
                image: example:1
            """);

        var (exit, output) = Run(prereq, compose);

        Assert.NotEqual(0, exit);
        Assert.Contains("vacuously", output, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_work, recursive: true);
        }
        catch (IOException)
        {
            // Test scratch space; a failure to clean it up is not a test failure.
        }
    }
}
