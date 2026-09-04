using System.Text.Json;
using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// Guards for the local Keycloak development dependency (#98).
/// </summary>
/// <remarks>
/// Standing up a container is verified by using it, and #98 says so rather than
/// inventing a unit test for a compose file — the end-to-end sign-in is recorded
/// on the issue. What is worth automating is the three properties that would
/// rot silently: that the profile stays off by default, that no admin credential
/// creeps into the repository, and that the realm export still contains what the
/// claim-mapping work needs to map.
///
/// The loopback binding needs nothing here: <see cref="ComposeLoopbackBindingTests"/>
/// scans every compose file in the tree, so the new service was covered the
/// moment it was written.
/// </remarks>
public sealed class KeycloakProfileTests
{
    private static string ComposePath =>
        Path.Combine(RepoRoot.Path, "infra", "docker-compose.yml");

    private static string RealmPath =>
        Path.Combine(RepoRoot.Path, "infra", "keycloak", "realm-export.json");

    private static string Compose() => File.ReadAllText(ComposePath);

    private static JsonElement Realm() =>
        JsonDocument.Parse(File.ReadAllText(RealmPath)).RootElement;

    [Fact]
    public void The_keycloak_service_sits_behind_a_profile()
    {
        // Off by default: the ordinary stack must be unchanged for anyone not
        // doing identity work, and a JVM is not free.
        var compose = Compose();
        var service = compose[compose.IndexOf("\n  keycloak:", StringComparison.Ordinal)..];
        var declaration = service[..service.IndexOf("\n  dapr-dashboard:", StringComparison.Ordinal)];

        Assert.Contains("profiles:", declaration, StringComparison.Ordinal);
        Assert.Contains("- keycloak", declaration, StringComparison.Ordinal);
    }

    [Fact]
    public void No_keycloak_admin_credential_is_committed()
    {
        // Invariant 1 holds for development dependencies too. A committed admin
        // password is exactly the defect this project already shipped once.
        var compose = Compose();
        var keycloak = compose[compose.IndexOf("\n  keycloak:", StringComparison.Ordinal)..];
        var declaration = keycloak[..keycloak.IndexOf("\n  dapr-dashboard:", StringComparison.Ordinal)];

        // Both admin variables must interpolate to nothing. `${VAR:-something}`
        // would be a working default wearing a variable's clothes.
        Assert.Contains("KC_BOOTSTRAP_ADMIN_USERNAME: ${AUTONATE_KEYCLOAK_ADMIN_USER:-}",
            declaration, StringComparison.Ordinal);
        Assert.Contains("KC_BOOTSTRAP_ADMIN_PASSWORD: ${AUTONATE_KEYCLOAK_ADMIN_PASSWORD:-}",
            declaration, StringComparison.Ordinal);

        // And the example file offers no value to copy.
        var example = File.ReadAllLines(Path.Combine(RepoRoot.Path, ".env.example"));
        foreach (var key in new[] { "AUTONATE_KEYCLOAK_ADMIN_USER", "AUTONATE_KEYCLOAK_ADMIN_PASSWORD" })
        {
            var line = Assert.Single(example.Where(l => l.StartsWith(key + "=", StringComparison.Ordinal)));
            Assert.Equal(key + "=", line);
        }
    }

    [Fact]
    public void The_realm_export_has_no_administrative_credential()
    {
        // The fixture user passwords ARE committed, deliberately: they exist
        // only inside a loopback-bound container rebuilt from this file on every
        // start, and a developer has to be able to type them into a login form.
        // What must never appear is a credential that grants control of the
        // identity provider, or a client secret — the OIDC client is public and
        // uses PKCE precisely so there is no secret to commit.
        var realm = Realm();

        var users = realm.GetProperty("users").EnumerateArray()
            .Select(u => u.GetProperty("username").GetString())
            .ToList();
        Assert.DoesNotContain("admin", users);
        Assert.All(users, u => Assert.DoesNotContain("admin", u!, StringComparison.OrdinalIgnoreCase));

        foreach (var client in realm.GetProperty("clients").EnumerateArray())
        {
            Assert.False(
                client.TryGetProperty("secret", out _),
                $"Client '{client.GetProperty("clientId").GetString()}' commits a secret. The OIDC "
                + "client is public + PKCE so it needs none; a confidential client here would put a "
                + "credential in the repository.");
        }
    }

    [Fact]
    public void The_realm_export_seeds_what_claim_mapping_needs()
    {
        // #92 maps an IdP group onto an Auton8 group, and has to work
        // identically through OIDC and SAML. A realm exposing groups through
        // only one of them would leave half of that untestable — and the half
        // left untested would be whichever was harder.
        var realm = Realm();

        var groups = realm.GetProperty("groups").EnumerateArray()
            .Select(g => g.GetProperty("name").GetString()).ToList();
        Assert.Contains("engineering", groups);
        Assert.Contains("sales", groups);

        var users = realm.GetProperty("users").EnumerateArray().ToList();
        Assert.True(users.Count >= 2, "At least two users, so membership can differ between them.");
        Assert.All(users, u => Assert.NotEmpty(u.GetProperty("groups").EnumerateArray().ToList()));

        var clients = realm.GetProperty("clients").EnumerateArray().ToList();
        var oidc = Assert.Single(clients.Where(c =>
            c.GetProperty("protocol").GetString() == "openid-connect"));
        var saml = Assert.Single(clients.Where(c =>
            c.GetProperty("protocol").GetString() == "saml"));

        Assert.Contains(
            oidc.GetProperty("protocolMappers").EnumerateArray(),
            m => m.GetProperty("config").TryGetProperty("claim.name", out var n)
                 && n.GetString() == "groups");

        Assert.Contains(
            saml.GetProperty("protocolMappers").EnumerateArray(),
            m => m.GetProperty("config").TryGetProperty("attribute.name", out var n)
                 && n.GetString() == "groups");
    }

    [Fact]
    public void The_oidc_client_is_public_and_requires_pkce()
    {
        // Public + PKCE is what lets the realm ship with no client secret at
        // all. If someone made it confidential, the secret would have to be
        // committed or the realm would stop working — so the property that
        // keeps a credential out of the repository is pinned here rather than
        // left to a comment.
        var oidc = Assert.Single(Realm().GetProperty("clients").EnumerateArray()
            .Where(c => c.GetProperty("protocol").GetString() == "openid-connect"));

        Assert.True(oidc.GetProperty("publicClient").GetBoolean());
        Assert.Equal(
            "S256",
            oidc.GetProperty("attributes").GetProperty("pkce.code.challenge.method").GetString());
    }

    [Fact]
    public void The_realm_is_re_imported_on_every_start_rather_than_persisted()
    {
        // No data volume, on purpose: each start re-imports from the checked-in
        // file, so the realm cannot drift away from its export. A persisted
        // database would let it, silently, and the next person would trust the
        // file anyway.
        var compose = Compose();
        var keycloak = compose[compose.IndexOf("\n  keycloak:", StringComparison.Ordinal)..];
        var declaration = keycloak[..keycloak.IndexOf("\n  dapr-dashboard:", StringComparison.Ordinal)];

        Assert.Contains("--import-realm", declaration, StringComparison.Ordinal);
        Assert.Contains("realm-export.json:ro", declaration, StringComparison.Ordinal);

        // The only volume is the read-only realm file.
        var volumeLines = declaration
            .Split('\n')
            .SkipWhile(l => !l.Contains("volumes:", StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(l => l.TrimStart().StartsWith('-') || l.TrimStart().StartsWith('#'))
            .Where(l => l.TrimStart().StartsWith('-'))
            .ToList();

        var volume = Assert.Single(volumeLines);
        Assert.EndsWith(":ro", volume.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_issuer_port_is_one_variable_used_on_both_sides()
    {
        // The whole issuer arrangement rests on the host port equalling the
        // container port. Two variables, or a literal on one side, would let
        // them drift — and the symptom would be an issuer mismatch deep inside
        // an OIDC library rather than anything pointing here.
        var compose = Compose();
        var keycloak = compose[compose.IndexOf("\n  keycloak:", StringComparison.Ordinal)..];
        var declaration = keycloak[..keycloak.IndexOf("\n  dapr-dashboard:", StringComparison.Ordinal)];

        const string Port = "${AUTONATE_KEYCLOAK_PORT:-8082}";

        Assert.Contains($"KC_HTTP_PORT: {Port}", declaration, StringComparison.Ordinal);
        Assert.Contains($"KC_HOSTNAME: http://keycloak:{Port}", declaration, StringComparison.Ordinal);
        Assert.Contains($"\"127.0.0.1:{Port}:{Port}\"", declaration, StringComparison.Ordinal);
    }
}
