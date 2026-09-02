using System.Net;
using System.Net.Sockets;
using AutoNate.Web.Services.Agent.Skills;
using Microsoft.Extensions.Hosting;

namespace AutoNate.Web.Services.Http;

/// <summary>
/// SSRF guard for outbound requests whose URL comes from user input, applied
/// <b>before any socket opens</b>: the scheme must be http/https, and the host
/// must be a public IP literal or a DNS name that resolves to only public
/// addresses. Private (RFC1918), loopback, link-local (including the
/// 169.254.169.254 cloud-metadata address), carrier-grade NAT, multicast and
/// IPv6 ULA/link-local answers are all refused.
/// </summary>
/// <remarks>
/// Use this where the set of legitimate destinations is open-ended and an
/// allowlist is therefore impossible — the REST data connector exists to call
/// arbitrary third-party APIs (archived-60). Where the legitimate hosts <i>are</i>
/// known, prefer <c>IProviderBaseUrlPolicy</c>: an allowlist is strictly
/// stronger, because it does not depend on classifying an address correctly.
///
/// Known limitation: this validates the addresses the resolver returns, then
/// hands the URL to <see cref="System.Net.Http.HttpClient"/>, which resolves
/// again. A DNS entry that flips between a public and a private address
/// between the two lookups (DNS rebinding) is not defeated by this check.
/// Closing that needs a pinned <c>ConnectCallback</c> on the handler; it is
/// out of scope here and tracked separately.
/// </remarks>
public interface IOutboundUrlGuard
{
    Task<OutboundUrlCheck> CheckAsync(
        string? url, OutboundUrlPolicy policy, CancellationToken cancellationToken = default);
}

/// <param name="RequireHttps">
/// Refuse plain http. Callers that carry a credential or run in production
/// should set this; the agent's web-fetch tool deliberately does not, because
/// fetching an http page is the feature.
/// </param>
public sealed record OutboundUrlPolicy(bool RequireHttps)
{
    public static readonly OutboundUrlPolicy AllowHttp = new(RequireHttps: false);
    public static readonly OutboundUrlPolicy HttpsOnly = new(RequireHttps: true);
}

public sealed record OutboundUrlCheck(bool Allowed, Uri? Uri, string? Error)
{
    public static OutboundUrlCheck Ok(Uri uri) => new(true, uri, null);

    public static OutboundUrlCheck Refused(string error) => new(false, null, error);
}

public sealed class OutboundUrlGuard : IOutboundUrlGuard
{
    private readonly IDnsResolver _dns;

    public OutboundUrlGuard(IDnsResolver dns)
    {
        _dns = dns;
    }

    public async Task<OutboundUrlCheck> CheckAsync(
        string? url, OutboundUrlPolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (string.IsNullOrWhiteSpace(url))
        {
            return OutboundUrlCheck.Refused("URL is required.");
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return OutboundUrlCheck.Refused($"'{url}' is not an absolute URI.");
        }
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return OutboundUrlCheck.Refused($"Only http and https are allowed; got '{uri.Scheme}'.");
        }
        if (policy.RequireHttps && uri.Scheme != Uri.UriSchemeHttps)
        {
            return OutboundUrlCheck.Refused("https is required for this request; got 'http'.");
        }

        IPAddress[] addresses;
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            if (!IPAddress.TryParse(uri.Host, out var literal))
            {
                return OutboundUrlCheck.Refused($"Could not parse host '{uri.Host}' as an IP literal.");
            }
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await _dns.ResolveAsync(uri.Host, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return OutboundUrlCheck.Refused($"DNS resolution failed for '{uri.Host}': {ex.Message}");
            }
            if (addresses.Length == 0)
            {
                return OutboundUrlCheck.Refused($"DNS returned no addresses for '{uri.Host}'.");
            }
        }

        // Every answer must be public: a name that resolves to both a public
        // and a private address is refused, otherwise the private one is
        // reachable on a retry.
        foreach (var address in addresses)
        {
            if (OutboundAddressRules.IsBlocked(address))
            {
                return OutboundUrlCheck.Refused(
                    $"Refusing to connect to private/link-local address {address}.");
            }
        }

        return OutboundUrlCheck.Ok(uri);
    }
}

/// <summary>
/// Address classification shared by every outbound guard. Single copy on
/// purpose — this used to live on <c>WebFetchSkill</c>, and archived-60 was partly a
/// consequence of it not being reachable from anywhere else.
/// </summary>
public static class OutboundAddressRules
{
    public static bool IsBlocked(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // Unwrap IPv4-mapped IPv6 (::ffff:a.b.c.d) so v4 ranges still match.
        var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        if (IPAddress.IsLoopback(ip)) return true; // 127.0.0.0/8 + ::1

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 0) return true;                                            // 0.0.0.0/8 "this network"
            if (b[0] == 10) return true;                                           // 10.0.0.0/8
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;              // 172.16.0.0/12
            if (b[0] == 192 && b[1] == 168) return true;                           // 192.168.0.0/16
            if (b[0] == 169 && b[1] == 254) return true;                           // 169.254.0.0/16 link-local + cloud metadata
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;             // 100.64.0.0/10 carrier-grade NAT
            if (b[0] >= 224) return true;                                          // multicast + reserved
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal) return true;
            if (ip.IsIPv6SiteLocal) return true;
            if (ip.IsIPv6Multicast) return true;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return true;                                // fc00::/7 ULA
            if (ip.Equals(IPAddress.IPv6Any)) return true;
        }

        return false;
    }
}

/// <summary>
/// Resolves the effective <see cref="OutboundUrlPolicy"/> for callers that
/// should demand https in real environments but stay usable against a plain
/// http endpoint on a developer's machine.
/// </summary>
public static class OutboundUrlPolicies
{
    public static OutboundUrlPolicy ForEnvironment(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment.IsDevelopment() ? OutboundUrlPolicy.AllowHttp : OutboundUrlPolicy.HttpsOnly;
    }
}
