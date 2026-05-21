namespace AutoNate.Web.Configuration;

// Controls whether X-Forwarded-For is trusted for the recorded client IP.
//
// Secure-by-default: when Enabled is false (the default), the app ignores
// X-Forwarded-For entirely and the audit IP comes straight from the TCP
// peer (HttpContext.Connection.RemoteIpAddress). Operators behind a real
// reverse proxy / load balancer set Enabled = true AND name the trusted
// upstream(s) in KnownProxies / KnownNetworks; only then does ASP.NET's
// ForwardedHeaders middleware promote the forwarded address into
// Connection.RemoteIpAddress.
//
// Behavior change note: prior to this option being introduced,
// X-Forwarded-For was honored unconditionally. Deployments that already
// run behind a stripping proxy must opt in here to keep recording the
// real client IP — or accept that auditContext.ipAddress will now show
// the proxy's IP.
public sealed class TrustedProxyOptions
{
    public const string SectionName = "TrustedProxy";

    public bool Enabled { get; set; }

    // Individual proxy IPs that are allowed to set X-Forwarded-For.
    public List<string> KnownProxies { get; set; } = new();

    // CIDR ranges of proxies allowed to set X-Forwarded-For (e.g.
    // "10.0.0.0/8" for an AWS VPC, "172.16.0.0/12" for a docker network).
    public List<string> KnownNetworks { get; set; } = new();

    // Maximum number of forwarded entries to walk. 1 = single proxy hop
    // (the common case). Bump if your edge has multiple trusted hops
    // (e.g. ALB → nginx → app == 2).
    public int ForwardLimit { get; set; } = 1;
}
