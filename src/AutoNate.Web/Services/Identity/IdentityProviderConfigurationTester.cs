using System.Text.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Services.ExternalConnections;

namespace AutoNate.Web.Services.Identity;

public interface IIdentityProviderConfigurationTester
{
    Task<IdentityProviderTestResult> TestAsync(IdentityProviderDto provider, CancellationToken ct);
}

/// <summary>
/// Reaches a provider's discovery or metadata endpoint and reports what it found.
/// </summary>
/// <remarks>
/// The point is that a typo in an issuer URL is reported when the provider is
/// saved, not at someone's first sign-in attempt — at which point the person
/// hitting it is a user who cannot get in and has no way to see why.
///
/// Every outbound request goes through <see cref="IProviderBaseUrlPolicy"/>,
/// the same allowlist that governs external connections. An identity provider's
/// host is typed in by an administrator, so an allowlist is the right control:
/// it cannot be defeated by a DNS answer, and it does not have to be right
/// about what "internal" means.
///
/// Failures are reported, never thrown. An unreachable IdP is the ordinary case
/// this action exists to detect, and a 500 would tell the administrator less
/// than the message does.
/// </remarks>
public sealed class IdentityProviderConfigurationTester : IIdentityProviderConfigurationTester
{
    /// <summary>Allowlist keys, one per kind, matching ProviderBaseUrlPolicy's convention.</summary>
    public const string OidcPolicyKind = ProviderBaseUrlPolicy.IdentityProviderKindPrefix + "Oidc";
    public const string SamlPolicyKind = ProviderBaseUrlPolicy.IdentityProviderKindPrefix + "Saml";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProviderBaseUrlPolicy _urlPolicy;
    private readonly ILogger<IdentityProviderConfigurationTester> _log;

    public IdentityProviderConfigurationTester(
        IHttpClientFactory httpClientFactory,
        IProviderBaseUrlPolicy urlPolicy,
        ILogger<IdentityProviderConfigurationTester> log)
    {
        _httpClientFactory = httpClientFactory;
        _urlPolicy = urlPolicy;
        _log = log;
    }

    public async Task<IdentityProviderTestResult> TestAsync(IdentityProviderDto provider, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return provider.Kind switch
        {
            IdentityProviderKinds.Oidc => await TestOidcAsync(provider, ct),
            IdentityProviderKinds.Saml => await TestSamlAsync(provider, ct),
            _ => Fail($"Unknown provider kind '{provider.Kind}'."),
        };
    }

    private async Task<IdentityProviderTestResult> TestOidcAsync(IdentityProviderDto provider, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(provider.OidcAuthority))
        {
            return Fail("No authority is configured. Set the issuer or discovery URL first.");
        }

        // Accept either the issuer or a full discovery URL, since administrators
        // paste both and the difference is not obvious from the IdP's own UI.
        var authority = provider.OidcAuthority.TrimEnd('/');
        var discovery = authority.EndsWith("/.well-known/openid-configuration", StringComparison.OrdinalIgnoreCase)
            ? authority
            : authority + "/.well-known/openid-configuration";

        Uri uri;
        try
        {
            uri = _urlPolicy.Resolve(OidcPolicyKind, discovery, discovery);
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ex.Message);
        }

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var response = await client.GetAsync(uri, ct);

            if (!response.IsSuccessStatusCode)
            {
                return Fail($"The discovery document at {uri} returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var findings = new List<string>();
            foreach (var required in new[] { "issuer", "authorization_endpoint", "token_endpoint", "jwks_uri" })
            {
                findings.Add(root.TryGetProperty(required, out var v)
                    ? $"{required}: {v.GetString()}"
                    : $"MISSING {required} — the provider will not be usable without it.");
            }

            var missing = findings.Count(f => f.StartsWith("MISSING", StringComparison.Ordinal));
            return new IdentityProviderTestResult(
                Success: missing == 0,
                Summary: missing == 0
                    ? $"Reached the discovery document at {uri}."
                    : $"Reached {uri}, but {missing} required field(s) are absent.",
                Findings: findings);
        }
        catch (JsonException)
        {
            return Fail($"{uri} did not return a JSON discovery document. Check that the authority is the issuer URL, not the login page.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogInformation(ex, "Identity provider configuration test failed for {Uri}.", uri);
            return Fail($"Could not reach {uri}: {ex.Message}");
        }
    }

    private async Task<IdentityProviderTestResult> TestSamlAsync(IdentityProviderDto provider, CancellationToken ct)
    {
        // Pasted metadata needs no network call — checking it parses is the
        // whole test, and is worth doing because pasted XML is where a
        // truncated copy shows up.
        if (provider.HasSamlMetadataXml && string.IsNullOrWhiteSpace(provider.SamlMetadataUrl))
        {
            return new IdentityProviderTestResult(
                Success: true,
                Summary: "Metadata is stored inline; no metadata URL to fetch.",
                Findings: ["Inline SAML metadata is present. It is validated when the provider is used."]);
        }

        if (string.IsNullOrWhiteSpace(provider.SamlMetadataUrl))
        {
            return Fail("No metadata URL and no inline metadata. Provide one of them.");
        }

        Uri uri;
        try
        {
            uri = _urlPolicy.Resolve(SamlPolicyKind, provider.SamlMetadataUrl, provider.SamlMetadataUrl);
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ex.Message);
        }

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var response = await client.GetAsync(uri, ct);

            if (!response.IsSuccessStatusCode)
            {
                return Fail($"The metadata at {uri} returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            var findings = new List<string>();

            if (!body.Contains("EntityDescriptor", StringComparison.Ordinal))
            {
                return Fail($"{uri} returned a document with no EntityDescriptor — it does not look like SAML metadata.");
            }

            findings.Add($"Fetched {body.Length:N0} bytes of SAML metadata.");
            findings.Add(body.Contains("IDPSSODescriptor", StringComparison.Ordinal)
                ? "IDPSSODescriptor present."
                : "MISSING IDPSSODescriptor — this metadata does not describe an identity provider.");

            var ok = findings.All(f => !f.StartsWith("MISSING", StringComparison.Ordinal));
            return new IdentityProviderTestResult(
                ok, ok ? $"Reached the metadata at {uri}." : $"Reached {uri}, but the document is not usable.", findings);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogInformation(ex, "Identity provider metadata test failed for {Uri}.", uri);
            return Fail($"Could not reach {uri}: {ex.Message}");
        }
    }

    private static IdentityProviderTestResult Fail(string message) => new(false, message, []);
}
