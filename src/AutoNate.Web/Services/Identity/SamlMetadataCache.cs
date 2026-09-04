using AutoNate.Web.Services.ExternalConnections;
using ITfoxtec.Identity.Saml2.Schemas.Metadata;

namespace AutoNate.Web.Services.Identity;

public interface ISamlMetadataCache
{
    /// <summary>
    /// Fetches and parses an IdP metadata document, or returns null if it cannot be had.
    /// </summary>
    Task<EntityDescriptor?> GetAsync(string metadataUrl, CancellationToken ct);
}

/// <summary>
/// Fetches IdP metadata documents and remembers them for a while.
/// </summary>
/// <remarks>
/// #93's acceptance criteria say an administrator configures the IdP "from
/// metadata — a URL or pasted XML". Pasted XML needs nothing beyond a parser;
/// a URL needs this, or the URL is decoration and only the paste path works.
///
/// Cached because the alternative is an outbound HTTP request on every sign-in:
/// slow for the user, and a dependency that makes an IdP's own web server an
/// availability risk for a protocol that does not otherwise need it. An hour
/// keeps certificate rollovers timely without making the IdP part of the
/// request path.
///
/// The URL goes through <see cref="IProviderBaseUrlPolicy"/> — the same
/// allowlist the configuration tester uses, and for the same reason: this is an
/// administrator-supplied URL that the server fetches, which is SSRF surface. An
/// allowlist cannot be defeated by a DNS answer and does not have to be right
/// about what "internal" means.
/// </remarks>
public sealed class SamlMetadataCache : ISamlMetadataCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProviderBaseUrlPolicy _urlPolicy;
    private readonly TimeProvider _clock;
    private readonly ILogger<SamlMetadataCache> _log;

    private readonly Dictionary<string, (EntityDescriptor Descriptor, DateTimeOffset Until)> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SamlMetadataCache(
        IHttpClientFactory httpClientFactory,
        IProviderBaseUrlPolicy urlPolicy,
        TimeProvider clock,
        ILogger<SamlMetadataCache> log)
    {
        _httpClientFactory = httpClientFactory;
        _urlPolicy = urlPolicy;
        _clock = clock;
        _log = log;
    }

    public async Task<EntityDescriptor?> GetAsync(string metadataUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(metadataUrl)) return null;

        await _gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(metadataUrl, out var hit) && hit.Until > _clock.GetUtcNow())
            {
                return hit.Descriptor;
            }

            Uri uri;
            try
            {
                uri = _urlPolicy.Resolve(
                    IdentityProviderConfigurationTester.SamlPolicyKind, metadataUrl, metadataUrl);
            }
            catch (InvalidOperationException ex)
            {
                _log.LogWarning(
                    "SAML metadata at {Url} was not fetched: {Reason}", metadataUrl, ex.Message);
                return null;
            }

            string body;
            try
            {
                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                using var response = await client.GetAsync(uri, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _log.LogWarning(
                        "SAML metadata at {Uri} returned {Status}.", uri, (int)response.StatusCode);
                    return null;
                }
                body = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _log.LogWarning(ex, "SAML metadata at {Uri} could not be fetched.", uri);
                return null;
            }

            EntityDescriptor descriptor;
            try
            {
                descriptor = new EntityDescriptor();
                descriptor.ReadIdPSsoDescriptor(body);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "SAML metadata at {Uri} could not be parsed.", uri);
                return null;
            }

            // Only a successful parse is cached. Caching a failure would turn a
            // momentary outage at the IdP into an hour of refused sign-ins.
            _cache[metadataUrl] = (descriptor, _clock.GetUtcNow() + Lifetime);
            return descriptor;
        }
        finally
        {
            _gate.Release();
        }
    }
}
