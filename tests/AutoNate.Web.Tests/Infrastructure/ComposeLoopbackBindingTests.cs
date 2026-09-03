using System.Text;
using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// Guard for project invariant 5: every published port in a shipped compose
/// file binds to loopback.
/// </summary>
/// <remarks>
/// The stack ships known credentials and an unauthenticated NATS. A
/// <c>0.0.0.0</c> bind therefore puts a writable database and a queue anyone can
/// publish to on whatever network the machine is attached to — a coffee shop, a
/// client office, a conference. Every port is compliant today and nothing
/// enforced it; the next service added would have been written
/// <c>"8080:8080"</c>, because that is what every compose example looks like.
///
/// Exceptions exist for services that deliberately mimic an out-of-network
/// dependency, but must carry a written reason beside the port so the exemption
/// cannot be made silently.
/// </remarks>
public sealed class ComposeLoopbackBindingTests
{
    private static string FixturePath(string name) =>
        Path.Combine(
            RepoRoot.Path, "tests", "AutoNate.Web.Tests", "Infrastructure", "ComposeFixtures", name);

    private static IReadOnlyList<PortBinding> ScanFixture(string name) =>
        ComposeFileScanner.ScanFile(FixturePath(name));

    private static string Describe(PortBinding b) =>
        $"{b.File}:{b.Line} service '{b.Service}' publishes '{b.Entry}'";

    // ── The regression guard ────────────────────────────────────────────────

    [Fact]
    public void Discovery_finds_the_repositorys_compose_files()
    {
        var files = ComposeFileScanner.DiscoverComposeFiles();

        // A glob that silently matched nothing would make every other
        // assertion in this class vacuously true, which is the failure mode
        // that makes an infrastructure guard worse than none at all.
        Assert.NotEmpty(files);
        Assert.Contains(files, f => f.EndsWith("infra/docker-compose.yml", StringComparison.Ordinal));
    }

    [Fact]
    public void Workflow_files_are_not_mistaken_for_compose_files()
    {
        // `.github/workflows/ci.yml` declares `services:` under a job. The
        // discriminator is that a compose file's `services:` is top-level.
        var files = ComposeFileScanner.DiscoverComposeFiles();

        Assert.DoesNotContain(files, f => f.Contains(".github/workflows/", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_published_port_in_every_shipped_compose_file_binds_to_loopback()
    {
        var violations = new List<string>();
        var exceptions = new List<string>();

        foreach (var file in ComposeFileScanner.DiscoverComposeFiles())
        {
            foreach (var binding in ComposeFileScanner.ScanFile(file))
            {
                if (binding.IsLoopback)
                {
                    continue;
                }

                if (binding.HasValidException)
                {
                    exceptions.Add($"{Describe(binding)} — {binding.ExceptionReason}");
                    continue;
                }

                violations.Add(Describe(binding));
            }
        }

        // Exceptions in force are printed even on success, so reviewing them is
        // reading one test's output rather than auditing every compose file.
        if (exceptions.Count > 0)
        {
            Console.WriteLine("Loopback exceptions in force:");
            foreach (var e in exceptions)
            {
                Console.WriteLine("  " + e);
            }
        }

        Assert.True(violations.Count == 0, BuildFailureMessage(violations));
    }

    private static string BuildFailureMessage(IReadOnlyList<string> violations)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            $"{violations.Count} published port(s) are not bound to 127.0.0.1. The stack ships known");
        sb.AppendLine(
            "credentials and an unauthenticated NATS, so this exposes a writable database on the");
        sb.AppendLine("machine's network. Bind to 127.0.0.1, or add a `# loopback-exception: <reason>`");
        sb.AppendLine("comment beside the port saying why it must be reachable off-host.");
        sb.AppendLine();
        foreach (var v in violations)
        {
            sb.AppendLine("  " + v);
        }

        return sb.ToString();
    }

    // ── The failure path, proven by fixtures ────────────────────────────────

    [Fact]
    public void Compliant_fixture_has_no_violations()
    {
        var bindings = ScanFixture("compliant.yml");

        Assert.Equal(3, bindings.Count);
        Assert.All(bindings, b => Assert.True(b.IsLoopback, Describe(b)));
    }

    [Fact]
    public void Interpolated_port_is_parsed_without_mistaking_the_default_for_a_host_ip()
    {
        // `${AUTONATE_POSTGRES_PORT:-15432}` contains a colon that is not a
        // port separator. Splitting naively would read "127.0.0.1" as the
        // host IP only by accident and break on other shapes.
        var binding = Assert.Single(
            ScanFixture("compliant.yml").Where(b => b.Entry.Contains("${", StringComparison.Ordinal)));

        Assert.Equal("127.0.0.1", binding.HostIp);
        Assert.True(binding.IsLoopback);
    }

    [Fact]
    public void Missing_host_ip_is_a_violation_naming_service_and_port()
    {
        var binding = Assert.Single(ScanFixture("violating-no-host-ip.yml"));

        Assert.False(binding.IsLoopback);
        Assert.False(binding.HasValidException);
        Assert.Equal("web", binding.Service);
        Assert.Equal("8080:8080", binding.Entry);
        Assert.Contains("violating-no-host-ip.yml", Describe(binding), StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_any_address_is_a_violation()
    {
        var binding = Assert.Single(ScanFixture("violating-explicit-any.yml"));

        Assert.Equal("0.0.0.0", binding.HostIp);
        Assert.False(binding.IsLoopback);
    }

    [Fact]
    public void Long_object_form_is_understood_in_both_directions()
    {
        var compliant = Assert.Single(ScanFixture("long-form-compliant.yml"));
        Assert.Equal("127.0.0.1", compliant.HostIp);
        Assert.True(compliant.IsLoopback);

        var violating = Assert.Single(ScanFixture("long-form-violating.yml"));
        Assert.Null(violating.HostIp);
        Assert.False(violating.IsLoopback);
    }

    // ── The exception mechanism ─────────────────────────────────────────────

    [Fact]
    public void Exception_with_a_written_reason_is_accepted_and_reported()
    {
        var binding = Assert.Single(ScanFixture("exception-valid.yml"));

        Assert.False(binding.IsLoopback);
        Assert.True(binding.HasValidException);
        Assert.Contains("identity provider", binding.ExceptionReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Exception_with_an_empty_reason_is_not_accepted()
    {
        // Otherwise the marker becomes a bare mute — a way to silence the guard
        // without saying anything, which is worse than no guard because it
        // looks reviewed.
        var binding = Assert.Single(ScanFixture("exception-empty-reason.yml"));

        Assert.False(binding.IsLoopback);
        Assert.False(binding.HasValidException);
    }

    [Fact]
    public void Exception_can_be_attached_to_a_single_port_rather_than_the_whole_block()
    {
        var bindings = ScanFixture("exception-per-entry.yml");

        Assert.Equal(2, bindings.Count);

        var loopback = Assert.Single(bindings.Where(b => b.IsLoopback));
        Assert.Null(loopback.ExceptionReason);

        var excepted = Assert.Single(bindings.Where(b => !b.IsLoopback));
        Assert.True(excepted.HasValidException);
        Assert.Contains("metrics port", excepted.ExceptionReason!, StringComparison.OrdinalIgnoreCase);
    }
}
