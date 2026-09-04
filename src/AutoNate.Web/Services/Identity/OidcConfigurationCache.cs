using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace AutoNate.Web.Services.Identity;

/// <summary>
/// Caches OIDC discovery documents and their signing keys, one manager per
/// metadata address.
/// </summary>
/// <remarks>
/// The library's <see cref="ConfigurationManager{T}"/> does the real work:
/// fetching the document, retrieving the JWKS, and refreshing both so a key
/// rollover at the provider does not require a restart here. What this adds is
/// one manager per address, reused.
///
/// It is a service rather than a static field inside the sign-in service, and
/// that distinction earned itself immediately: as a static it was process-wide
/// mutable state keyed only on the URL, so two providers that happened to share
/// an authority — or, as it turned out, two tests using the same authority with
/// different signing keys — got each other's keys. The first symptom was a suite
/// where every test passed alone and half failed together, which is a bad way to
/// find out that a cache is shared more widely than intended.
/// </remarks>
public interface IOidcConfigurationCache
{
    Task<OpenIdConnectConfiguration> GetAsync(string metadataAddress, CancellationToken ct);
}

public sealed class OidcConfigurationCache : IOidcConfigurationCache
{
    private readonly Dictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _managers = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IHttpClientFactory _httpClientFactory;

    public OidcConfigurationCache(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<OpenIdConnectConfiguration> GetAsync(string metadataAddress, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!_managers.TryGetValue(metadataAddress, out var manager))
            {
                manager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    metadataAddress,
                    new OpenIdConnectConfigurationRetriever(),
                    new HttpDocumentRetriever(_httpClientFactory.CreateClient())
                    {
                        // Whether a provider may be reached over plain http is
                        // decided once, at configuration time, by
                        // ProviderBaseUrlPolicy (#87) — Development only, and
                        // only for allowlisted identity-provider hosts. By the
                        // time a provider is enabled its address has passed
                        // that gate, so re-litigating it here would only mean
                        // the local Keycloak in #98 could never be used.
                        RequireHttps = false,
                    });
                _managers[metadataAddress] = manager;
            }
            return await manager.GetConfigurationAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }
}
