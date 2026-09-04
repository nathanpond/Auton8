using System.Net.Http.Json;
using System.Text.Json;

namespace AutoNate.E2E.Tests.Support;

/// <summary>
/// Talks to the local Keycloak (#98) so the interop specs can run against real
/// identity-provider software.
/// </summary>
/// <remarks>
/// The realm's users, groups and group mappers are seeded from the checked-in
/// export and are not touched here — those are the parts #92 needs and the parts
/// that do not depend on a port.
///
/// The two <em>clients</em> are provisioned per run, which is a deviation from
/// #99's key_link ("pointing at the seeded clients") with a reason:
/// <see cref="AutoNateE2EFixture"/> starts the app on a random Kestrel port, and
/// both protocols pin the port. OIDC needs the exact <c>redirect_uri</c>
/// pre-registered; SAML is worse, because the SP entity ID *is* the client ID
/// and is derived from the request host, so it changes with the port.
///
/// The obvious escape does not work, and was tried before this was written:
/// Keycloak accepts <c>http://127.0.0.1:*/api/auth/oidc/*</c> through the admin
/// API (204) and then rejects it at authorization time with
/// <c>Invalid parameter: redirect_uri</c>. Only trailing wildcards are honoured.
/// </remarks>
internal sealed class KeycloakRealm : IAsyncDisposable
{
    internal const string Realm = "auton8";
    internal const string OidcClientId = "auton8-oidc";

    /// <summary>Seeded users, with the groups the export puts them in.</summary>
    internal const string AliceUsername = "alice";
    internal const string AlicePassword = "alice";
    internal const string EngineeringGroup = "engineering";

    private readonly HttpClient _http = new();
    private string _token = string.Empty;

    private KeycloakRealm(string baseUrl) => BaseUrl = baseUrl;

    /// <summary>Where Keycloak lives, as both the browser and the app must reach it.</summary>
    internal string BaseUrl { get; }

    /// <summary>
    /// Connects, or returns null when Keycloak is not running.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception so a developer without the profile running
    /// gets a skipped spec with an explanation, not a stack trace. CI never
    /// reaches this — the specs carry <c>RequiresService=Keycloak</c> and are
    /// filtered out — so this path exists purely for the local case.
    /// </remarks>
    internal static async Task<KeycloakRealm?> ConnectAsync()
    {
        var baseUrl = Environment.GetEnvironmentVariable("AUTONATE_KEYCLOAK_URL")
            ?? "http://keycloak:8082";
        var user = Environment.GetEnvironmentVariable("AUTONATE_KEYCLOAK_ADMIN_USER");
        var password = Environment.GetEnvironmentVariable("AUTONATE_KEYCLOAK_ADMIN_PASSWORD");

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
        {
            (user, password) = ReadAdminCredentialsFromEnvFile();
        }

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password)) return null;

        var realm = new KeycloakRealm(baseUrl);
        try
        {
            var response = await realm._http.PostAsync(
                $"{baseUrl}/realms/master/protocol/openid-connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = "admin-cli",
                    ["username"] = user!,
                    ["password"] = password!,
                    ["grant_type"] = "password",
                }));

            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            realm._token = json.GetProperty("access_token").GetString()!;
            realm._http.DefaultRequestHeaders.Authorization = new("Bearer", realm._token);
            return realm;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the admin credentials the developer put in <c>.env</c> for #98.
    /// </summary>
    /// <remarks>
    /// There is deliberately no default: invariant 1 holds for development
    /// dependencies, so a missing credential means "not configured", never
    /// "fall back to something committed".
    /// </remarks>
    private static (string? User, string? Password) ReadAdminCredentialsFromEnvFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, ".env")))
        {
            dir = dir.Parent;
        }

        if (dir is null) return (null, null);

        string? Read(string key) => File.ReadLines(Path.Combine(dir.FullName, ".env"))
            .Where(l => l.StartsWith(key + "=", StringComparison.Ordinal))
            .Select(l => l[(key.Length + 1)..].Trim())
            .LastOrDefault();

        return (Read("AUTONATE_KEYCLOAK_ADMIN_USER"), Read("AUTONATE_KEYCLOAK_ADMIN_PASSWORD"));
    }

    /// <summary>Points the seeded OIDC client at this run's app URL.</summary>
    internal async Task ConfigureOidcClientAsync(string appBaseUrl)
    {
        var id = await ClientUuidAsync(OidcClientId)
            ?? throw new InvalidOperationException(
                $"The seeded realm has no '{OidcClientId}' client. Recreate the container so it "
                + "re-imports infra/keycloak/realm-export.json.");

        await PutAsync($"clients/{id}", new
        {
            redirectUris = new[] { $"{appBaseUrl}/api/auth/oidc/*" },
            webOrigins = new[] { appBaseUrl },
        });
    }

    /// <summary>
    /// Creates a SAML client whose id is this run's SP entity ID.
    /// </summary>
    /// <remarks>
    /// Created rather than edited: the entity ID is the client id in SAML, so a
    /// new port is a new client. Deleted on dispose so a run does not leave the
    /// realm carrying every port it has ever used.
    /// </remarks>
    internal async Task<string> CreateSamlClientAsync(string appBaseUrl, string slug)
    {
        var entityId = $"{appBaseUrl}/api/auth/saml/{slug}/metadata";
        var acs = $"{appBaseUrl}/api/auth/saml/{slug}/acs";

        var existing = await ClientUuidAsync(entityId);
        if (existing is not null) await DeleteAsync($"clients/{existing}");

        var response = await _http.PostAsJsonAsync($"{BaseUrl}/admin/realms/{Realm}/clients", new
        {
            clientId = entityId,
            name = "Auton8 (SAML, E2E)",
            enabled = true,
            protocol = "saml",
            publicClient = true,
            redirectUris = new[] { $"{appBaseUrl}/api/auth/saml/*" },
            adminUrl = acs,
            attributes = new Dictionary<string, string>
            {
                ["saml.assertion.signature"] = "true",
                ["saml.server.signature"] = "false",
                ["saml.client.signature"] = "false",
                ["saml.force.post.binding"] = "true",
                ["saml_name_id_format"] = "persistent",
                ["saml_assertion_consumer_url_post"] = acs,
                ["saml.signature.algorithm"] = "RSA_SHA256",
            },
            protocolMappers = new object[]
            {
                new
                {
                    name = "groups",
                    protocol = "saml",
                    protocolMapper = "saml-group-membership-mapper",
                    config = new Dictionary<string, string>
                    {
                        ["full.path"] = "false",
                        ["attribute.name"] = "groups",
                        ["attribute.nameformat"] = "Basic",
                        ["single"] = "false",
                    },
                },
                new
                {
                    name = "email",
                    protocol = "saml",
                    protocolMapper = "saml-user-property-mapper",
                    config = new Dictionary<string, string>
                    {
                        ["user.attribute"] = "email",
                        ["attribute.name"] =
                            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress",
                        ["attribute.nameformat"] = "Basic",
                    },
                },
            },
        });

        response.EnsureSuccessStatusCode();
        _createdClients.Add(entityId);
        return entityId;
    }

    /// <summary>The realm's SAML metadata, for Auton8's provider configuration.</summary>
    internal string SamlDescriptorUrl => $"{BaseUrl}/realms/{Realm}/protocol/saml/descriptor";

    /// <summary>
    /// The realm's SAML metadata document itself.
    /// </summary>
    /// <remarks>
    /// The specs paste this rather than pointing Auton8 at
    /// <see cref="SamlDescriptorUrl"/>, because the URL path fetches through
    /// the allowlist and that allowlist cannot be extended from configuration
    /// at all — a colon in the kind key is read as nesting, so the entry binds
    /// to an empty array (#137).
    ///
    /// This is not a workaround that weakens the test: it is the same document
    /// from the same real Keycloak, and it exercises the paste path an
    /// administrator uses when Auton8 cannot reach the IdP directly. When #137
    /// is fixed, a spec pointing at the URL is worth adding beside this one.
    /// </remarks>
    internal async Task<string> SamlDescriptorXmlAsync() =>
        await _http.GetStringAsync(SamlDescriptorUrl);

    /// <summary>The realm's issuer, for Auton8's OIDC provider configuration.</summary>
    internal string Issuer => $"{BaseUrl}/realms/{Realm}";

    internal async Task<string> UserIdAsync(string username)
    {
        var users = await _http.GetFromJsonAsync<JsonElement>(
            $"{BaseUrl}/admin/realms/{Realm}/users?username={username}&exact=true");
        return users.EnumerateArray().First().GetProperty("id").GetString()!;
    }

    internal async Task<string> GroupIdAsync(string name)
    {
        var groups = await _http.GetFromJsonAsync<JsonElement>(
            $"{BaseUrl}/admin/realms/{Realm}/groups");
        return groups.EnumerateArray()
            .First(g => g.GetProperty("name").GetString() == name)
            .GetProperty("id").GetString()!;
    }

    internal async Task RemoveFromGroupAsync(string userId, string groupId) =>
        await DeleteAsync($"users/{userId}/groups/{groupId}");

    internal async Task AddToGroupAsync(string userId, string groupId)
    {
        var response = await _http.PutAsync(
            $"{BaseUrl}/admin/realms/{Realm}/users/{userId}/groups/{groupId}", content: null);
        response.EnsureSuccessStatusCode();
    }

    internal async Task SetEmailAsync(string userId, string email) =>
        await PutAsync($"users/{userId}", new { email });

    private readonly List<string> _createdClients = [];

    private async Task<string?> ClientUuidAsync(string clientId)
    {
        var encoded = Uri.EscapeDataString(clientId);
        var clients = await _http.GetFromJsonAsync<JsonElement>(
            $"{BaseUrl}/admin/realms/{Realm}/clients?clientId={encoded}");
        return clients.EnumerateArray().Any()
            ? clients.EnumerateArray().First().GetProperty("id").GetString()
            : null;
    }

    private async Task PutAsync(string path, object body)
    {
        var response = await _http.PutAsJsonAsync($"{BaseUrl}/admin/realms/{Realm}/{path}", body);
        response.EnsureSuccessStatusCode();
    }

    private async Task DeleteAsync(string path)
    {
        var response = await _http.DeleteAsync($"{BaseUrl}/admin/realms/{Realm}/{path}");
        response.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        // Leave the realm as it was found. The container re-imports on every
        // start, so this is belt-and-braces — but a spec run that mutates shared
        // state and does not put it back is how the next run passes for the
        // wrong reason.
        foreach (var clientId in _createdClients)
        {
            try
            {
                var uuid = await ClientUuidAsync(clientId);
                if (uuid is not null) await DeleteAsync($"clients/{uuid}");
            }
            catch (HttpRequestException)
            {
                // Keycloak went away; the next container start re-imports anyway.
            }
        }

        _http.Dispose();
    }
}
