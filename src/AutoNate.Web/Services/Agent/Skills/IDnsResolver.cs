using System.Net;

namespace AutoNate.Web.Services.Agent.Skills;

// Tiny abstraction so WebFetchSkill's SSRF guard can be tested without real
// DNS. Production wiring goes through Dns.GetHostAddressesAsync; tests pass a
// fake that maps known hosts to canned IP lists.
public interface IDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemDnsResolver : IDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}
