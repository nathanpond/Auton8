using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Services.ExternalConnections;

/// <summary>
/// Decides which host a provider credential may be sent to (archived-61).
/// </summary>
/// <remarks>
/// The <c>baseUrl</c> on an external connection is operator-supplied metadata
/// that ends up on requests carrying the decrypted API key — on every chat
/// turn, not just when an admin presses "list models". Pointing a connection
/// at an attacker's host therefore hands them the key, and feeds their
/// response text straight into the agent's context.
///
/// The legitimate destinations for a given provider kind are a short known
/// list, so this is an allowlist rather than the private-IP classification
/// used by <c>IOutboundUrlGuard</c>: an allowlist cannot be defeated by a DNS
/// answer, and it does not have to be right about what "internal" means.
/// Operators extend it per kind through <c>ExternalConnections:AllowedProviderHosts</c>
/// for Azure OpenAI, a corporate gateway, or a locally hosted model.
/// </remarks>
public interface IProviderBaseUrlPolicy
{
    /// <summary>
    /// Validates <paramref name="baseUrl"/> for <paramref name="kind"/> and returns the
    /// URI to use, falling back to <paramref name="defaultBaseUrl"/> when none is set.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The URL is unusable, not https, or its host is not allowlisted for this kind.
    /// </exception>
    Uri Resolve(string kind, string? baseUrl, string defaultBaseUrl);
}

public sealed class ExternalConnectionUrlOptions
{
    public const string SectionName = "ExternalConnections";

    /// <summary>
    /// Extra hosts an operator permits per connection kind, e.g.
    /// <c>{"LlmProvider:OpenAI": ["my-gateway.corp.example"]}</c>. Merged with
    /// the built-in defaults; matching is case-insensitive on host only (no
    /// port, no path), and a leading "*." makes it a subdomain wildcard.
    /// </summary>
    public Dictionary<string, string[]> AllowedProviderHosts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProviderBaseUrlPolicy : IProviderBaseUrlPolicy
{
    // The official endpoint for each kind the app ships a provider for.
    private static readonly Dictionary<string, string[]> BuiltInHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LlmProvider:Anthropic"] = ["api.anthropic.com"],
        ["LlmProvider:OpenAI"] = ["api.openai.com"],
        ["WebSearchProvider:Tavily"] = ["api.tavily.com"],
    };

    /// <summary>
    /// Kinds whose host is typed in by an administrator pointing at their own
    /// infrastructure, rather than a known SaaS endpoint.
    /// </summary>
    /// <remarks>
    /// These are the only kinds eligible for the Development plain-http
    /// accommodation below. Scoped deliberately: a relaxation that applied to
    /// every kind would also let a developer point an LLM connection carrying a
    /// real API key at an http host.
    /// </remarks>
    public const string IdentityProviderKindPrefix = "IdentityProvider:";

    private readonly IOptions<ExternalConnectionUrlOptions> _options;
    private readonly IHostEnvironment? _environment;

    public ProviderBaseUrlPolicy(
        IOptions<ExternalConnectionUrlOptions> options,
        IHostEnvironment? environment = null)
    {
        _options = options;
        _environment = environment;
    }

    /// <summary>
    /// Whether an allowlisted identity-provider host may be reached over plain
    /// http.
    /// </summary>
    /// <remarks>
    /// True only in Development, and only for identity-provider kinds. The
    /// decision is made here from <see cref="IHostEnvironment"/> rather than
    /// passed in by a caller, because a caller could pass true in production —
    /// #87 requires that this relaxation *cannot* be enabled outside
    /// Development, not merely that it is not.
    ///
    /// Without it the seeded Keycloak in #98 cannot be configured as a provider
    /// at all: it serves plain http on a loopback address in the local stack.
    /// </remarks>
    private bool PlainHttpPermitted(string kind) =>
        _environment is not null
        && _environment.IsDevelopment()
        && kind.StartsWith(IdentityProviderKindPrefix, StringComparison.OrdinalIgnoreCase);

    public Uri Resolve(string kind, string? baseUrl, string defaultBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultBaseUrl);

        // No override: the built-in default is trusted by definition.
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return new Uri(defaultBaseUrl);
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"Connection base URL '{baseUrl}' is not an absolute URI.");
        }

        if (uri.Scheme != Uri.UriSchemeHttps
            && !(uri.Scheme == Uri.UriSchemeHttp && PlainHttpPermitted(kind)))
        {
            throw new InvalidOperationException(
                $"Connection base URL must use https so the credential is not sent in the clear; got '{uri.Scheme}'.");
        }

        var allowed = AllowedHostsFor(kind);
        if (!allowed.Any(pattern => HostMatches(uri.Host, pattern)))
        {
            throw new InvalidOperationException(
                $"Host '{uri.Host}' is not an allowed endpoint for connection kind '{kind}'. " +
                $"Allowed: {string.Join(", ", allowed)}. Add it to " +
                $"{ExternalConnectionUrlOptions.SectionName}:AllowedProviderHosts:{kind} to permit it.");
        }

        return uri;
    }

    private IReadOnlyList<string> AllowedHostsFor(string kind)
    {
        var hosts = new List<string>();
        if (BuiltInHosts.TryGetValue(kind, out var builtIn))
        {
            hosts.AddRange(builtIn);
        }
        if (_options.Value.AllowedProviderHosts.TryGetValue(kind, out var configured) && configured is not null)
        {
            hosts.AddRange(configured.Where(h => !string.IsNullOrWhiteSpace(h)));
        }
        return hosts;
    }

    private static bool HostMatches(string host, string pattern)
    {
        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = pattern[1..]; // ".example.com"
            return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                   && host.Length > suffix.Length;
        }
        return string.Equals(host, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
