using System.Net;
using AutoNate.Web.Services.Agent.Skills;
using AutoNate.Web.Services.Http;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AutoNate.Web.Tests.Security;

// #60: the REST data connector fetched user-supplied URLs with no guard, so a
// connector pointed at 169.254.169.254 read cloud instance credentials out
// through the preview endpoint.
public sealed class OutboundUrlGuardTests
{
    [Theory]
    [InlineData("http://127.0.0.1/x")]
    [InlineData("http://[::1]/x")]
    [InlineData("http://169.254.169.254/computeMetadata/v1/")]   // cloud metadata
    [InlineData("http://10.0.0.5/internal")]
    [InlineData("http://172.20.5.4/internal")]
    [InlineData("http://192.168.1.1/router")]
    [InlineData("http://100.64.0.1/cgnat")]
    [InlineData("http://[fc00::1]/ula")]
    [InlineData("http://[fe80::1]/link-local")]
    [InlineData("http://0.0.0.0/this-network")]
    public async Task Private_ip_literals_are_refused_without_touching_dns(string url)
    {
        var guard = new OutboundUrlGuard(FakeDns.Strict());

        var result = await guard.CheckAsync(url, OutboundUrlPolicy.AllowHttp);

        Assert.False(result.Allowed);
        Assert.Contains("private/link-local", result.Error);
    }

    [Fact]
    public async Task Public_ip_literal_is_allowed()
    {
        var guard = new OutboundUrlGuard(FakeDns.Strict());

        var result = await guard.CheckAsync("https://8.8.8.8/probe", OutboundUrlPolicy.HttpsOnly);

        Assert.True(result.Allowed, result.Error);
        Assert.Equal("8.8.8.8", result.Uri!.Host);
    }

    [Fact]
    public async Task Hostname_resolving_to_a_private_address_is_refused()
    {
        var guard = new OutboundUrlGuard(FakeDns.Of("evil.example.com", "127.0.0.1"));

        var result = await guard.CheckAsync("https://evil.example.com/x", OutboundUrlPolicy.HttpsOnly);

        Assert.False(result.Allowed);
        Assert.Contains("127.0.0.1", result.Error);
    }

    // A name that answers with both a public and a private address must be
    // refused: allowing it would leave the private one reachable on a retry.
    [Fact]
    public async Task Hostname_resolving_to_mixed_public_and_private_is_refused()
    {
        var guard = new OutboundUrlGuard(FakeDns.Of("mixed.example.com", "1.1.1.1", "10.0.0.9"));

        var result = await guard.CheckAsync("https://mixed.example.com/x", OutboundUrlPolicy.HttpsOnly);

        Assert.False(result.Allowed);
        Assert.Contains("10.0.0.9", result.Error);
    }

    [Fact]
    public async Task Ipv4_mapped_ipv6_loopback_is_refused()
    {
        var guard = new OutboundUrlGuard(FakeDns.Of("sneaky.example.com", "::ffff:127.0.0.1"));

        var result = await guard.CheckAsync("https://sneaky.example.com/x", OutboundUrlPolicy.HttpsOnly);

        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task Public_hostname_is_allowed()
    {
        var guard = new OutboundUrlGuard(FakeDns.Of("api.example.com", "93.184.216.34"));

        var result = await guard.CheckAsync("https://api.example.com/rows", OutboundUrlPolicy.HttpsOnly);

        Assert.True(result.Allowed, result.Error);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/x")]
    [InlineData("gopher://example.com/x")]
    public async Task Non_http_schemes_are_refused(string url)
    {
        var guard = new OutboundUrlGuard(FakeDns.Strict());

        var result = await guard.CheckAsync(url, OutboundUrlPolicy.AllowHttp);

        Assert.False(result.Allowed);
        Assert.Contains("Only http and https", result.Error);
    }

    [Fact]
    public async Task Http_is_refused_when_the_policy_requires_https()
    {
        var guard = new OutboundUrlGuard(FakeDns.Of("api.example.com", "93.184.216.34"));

        var result = await guard.CheckAsync("http://api.example.com/rows", OutboundUrlPolicy.HttpsOnly);

        Assert.False(result.Allowed);
        Assert.Contains("https is required", result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("/relative/path")]
    public async Task Unusable_urls_are_refused(string? url)
    {
        var guard = new OutboundUrlGuard(FakeDns.Strict());

        var result = await guard.CheckAsync(url, OutboundUrlPolicy.AllowHttp);

        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task Dns_failure_is_refused_rather_than_thrown()
    {
        var guard = new OutboundUrlGuard(FakeDns.Throws("boom"));

        var result = await guard.CheckAsync("https://api.example.com/x", OutboundUrlPolicy.HttpsOnly);

        Assert.False(result.Allowed);
        Assert.Contains("DNS resolution failed", result.Error);
    }

    [Fact]
    public async Task Empty_dns_answer_is_refused()
    {
        var guard = new OutboundUrlGuard(FakeDns.Of("api.example.com"));

        var result = await guard.CheckAsync("https://api.example.com/x", OutboundUrlPolicy.HttpsOnly);

        Assert.False(result.Allowed);
        Assert.Contains("no addresses", result.Error);
    }

    [Fact]
    public void Environment_policy_requires_https_outside_development()
    {
        Assert.False(OutboundUrlPolicies.ForEnvironment(Env("Development")).RequireHttps);
        Assert.True(OutboundUrlPolicies.ForEnvironment(Env("Production")).RequireHttps);
        Assert.True(OutboundUrlPolicies.ForEnvironment(Env("Staging")).RequireHttps);
    }

    // The rules moved off WebFetchSkill so the guards cannot drift; the skill's
    // public helper must still answer identically.
    [Fact]
    public void WebFetchSkill_delegates_to_the_shared_rules()
    {
        foreach (var ip in new[] { "127.0.0.1", "10.0.0.1", "169.254.169.254", "fc00::1", "8.8.8.8", "1.1.1.1" })
        {
            var address = IPAddress.Parse(ip);
            Assert.Equal(OutboundAddressRules.IsBlocked(address), WebFetchSkill.IsBlockedAddress(address));
        }
    }

    private static IHostEnvironment Env(string name) => new StubEnvironment(name);

    private sealed class StubEnvironment : IHostEnvironment
    {
        public StubEnvironment(string environmentName) => EnvironmentName = environmentName;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    internal static class FakeDns
    {
        // Throws if queried at all — proves IP-literal paths never hit DNS.
        public static IDnsResolver Strict() => new Fake(null, strict: true, error: null);

        public static IDnsResolver Of(string host, params string[] ips) =>
            new Fake(new Dictionary<string, IPAddress[]>(StringComparer.OrdinalIgnoreCase)
            {
                [host] = ips.Select(IPAddress.Parse).ToArray()
            }, strict: false, error: null);

        public static IDnsResolver Throws(string message) => new Fake(null, strict: false, error: message);

        private sealed class Fake : IDnsResolver
        {
            private readonly IReadOnlyDictionary<string, IPAddress[]>? _table;
            private readonly bool _strict;
            private readonly string? _error;

            public Fake(IReadOnlyDictionary<string, IPAddress[]>? table, bool strict, string? error)
            {
                _table = table;
                _strict = strict;
                _error = error;
            }

            public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
            {
                if (_strict) throw new InvalidOperationException($"DNS unexpectedly queried for {host}");
                if (_error is not null) throw new InvalidOperationException(_error);
                if (_table is not null && _table.TryGetValue(host, out var ips)) return Task.FromResult(ips);
                return Task.FromResult(Array.Empty<IPAddress>());
            }
        }
    }
}
