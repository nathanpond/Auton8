using System.Text.RegularExpressions;
using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// The released stack must be runnable by someone who has never seen this
/// repository.
/// </summary>
public sealed class ReleaseComposeTests
{
    private static string Template() =>
        File.ReadAllText(Path.Combine(
            RepoRoot.Path, "infra", "release", "compose.template.yml"));

    private static string EnvTemplate() =>
        File.ReadAllText(Path.Combine(RepoRoot.Path, "infra", "release", "env.template"));

    [Fact]
    public void Every_image_is_a_digest_placeholder_or_a_digest()
    {
        // A bare tag here would undo the point of the release: an immutable
        // stack a consumer can verify.
        var bare = Regex.Matches(Template(), @"^\s+image: [^@\n]+$", RegexOptions.Multiline)
            .Select(m => m.Value.Trim())
            .ToList();

        Assert.True(bare.Count == 0,
            "These images are not pinned by digest:\n  " + string.Join("\n  ", bare));
    }

    [Fact]
    public void Nothing_references_a_source_tree()
    {
        // The consumer has a compose file and a .env in an empty directory.
        // A build context or a relative bind mount would refer to files they
        // do not have.
        var template = Template();

        Assert.DoesNotContain("build:", template);
        Assert.DoesNotContain("../", template);
        Assert.DoesNotContain("./mounts", template);
    }

    [Fact]
    public void Persistent_data_lives_in_named_volumes()
    {
        var template = Template();

        foreach (var volume in new[]
                 {
                     "postgres-data", "redis-data", "nats-data",
                     "dapr-scheduler-data", "autonate-web-data",
                 })
        {
            Assert.Contains(volume, template, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_published_port_binds_to_loopback()
    {
        // The same rule as the development stack (project invariant 5), and it
        // matters more here: this file is run by people who did not write it.
        // Entries look like "127.0.0.1:${APP_PORT:-5108}:8080" — the
        // interpolation contains its own colon, so match the whole quoted
        // string and filter on it ending in a container port rather than
        // trying to parse the middle.
        var ports = Regex.Matches(Template(), @"^\s+- ""(?<entry>[^""]+)""",
                RegexOptions.Multiline)
            .Select(m => m.Groups["entry"].Value)
            .Where(e => Regex.IsMatch(e, @":\d+$"))
            .ToList();

        Assert.NotEmpty(ports);
        Assert.All(ports, p =>
            Assert.True(p.StartsWith("127.0.0.1:", StringComparison.Ordinal),
                $"Published port '{p}' is not bound to loopback."));
    }

    [Fact]
    public void No_credential_is_shipped_and_the_required_ones_have_no_default()
    {
        var template = Template();
        var env = EnvTemplate();

        // The dev stack's password must not leak into a released file.
        Assert.DoesNotContain("Your_password123", template);
        Assert.DoesNotContain("Your_password123", env);
        Assert.DoesNotContain("dev-only-", template);
        Assert.DoesNotContain("dev-only-", env);

        // Required secrets use the `:?` form, so compose refuses to start
        // rather than substituting an empty string and failing later in a way
        // nobody can diagnose.
        foreach (var required in new[]
                 {
                     "POSTGRES_PASSWORD:?", "WORKFLOW_CALLBACK_SECRET:?", "YJS_SHARED_SECRET:?",
                 })
        {
            Assert.Contains(required, template, StringComparison.Ordinal);
        }

        // And the bootstrap admin is present but empty: nothing is seeded.
        Assert.Contains("Bootstrap__AdminUsername=\n", env, StringComparison.Ordinal);
        Assert.Contains("Bootstrap__AdminPassword=\n", env, StringComparison.Ordinal);
    }

    [Fact]
    public void Inlined_shell_variables_are_escaped_for_compose_interpolation()
    {
        // Compose interpolates $VAR inside inlined config content. Written as
        // $VAR the bootstrap script would receive an empty STREAM_NAME and
        // empty SUBJECTS, creating a nameless stream covering nothing — and
        // every publish afterwards failing with "no response from stream".
        // Compose only warns, and the warning is easy to skim past.
        var template = Template();

        Assert.Contains("$${STREAM_NAME}", template, StringComparison.Ordinal);
        Assert.Contains("$${SUBJECTS}", template, StringComparison.Ordinal);
        Assert.DoesNotContain("\"${STREAM_NAME}\"", template);
    }

    [Fact]
    public void The_released_stack_covers_the_same_services_as_the_development_stack()
    {
        // A service added to the dev stack and forgotten here produces a
        // release that is quietly missing a component.
        var dev = File.ReadAllText(Path.Combine(RepoRoot.Path, "infra", "docker-compose.yml"));
        var devServices = Regex.Matches(dev, @"^  (?<name>[a-z0-9-]+):$", RegexOptions.Multiline)
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var released = Regex.Matches(Template(), @"^  (?<name>[a-z0-9-]+):$", RegexOptions.Multiline)
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        // Deliberately absent, each for a stated reason.
        var excluded = new HashSet<string>(StringComparer.Ordinal)
        {
            "dapr-dashboard",   // dashboard profile: a debugging aid, not part of the product

            // keycloak profile: a development and testing dependency, not a
            // product component. Auton8 federates to whatever identity provider
            // an organisation already has and deliberately does not ship one
            // (#98; whether it ever should is #100, Post-1.0). Shipping it would
            // also put a second set of admin credentials in a released stack.
            "keycloak",
        };

        var missing = devServices.Except(released).Except(excluded).ToList();

        Assert.True(missing.Count == 0,
            "These services exist in the development stack but not in the released one. Add them, "
            + "or add them to the exclusion list with a reason:\n  " + string.Join("\n  ", missing));
    }
}
