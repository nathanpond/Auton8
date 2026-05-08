using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Services.Agent.Skills;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class WebFetchSkillTests
{
    [Fact]
    public async Task Returns_body_for_a_simple_text_response()
    {
        var stub = new StubHttpMessageHandler();
        stub.When(HttpMethod.Get, "/page", _ => TextResponse("Hello, world.", "text/plain"));

        var skill = CreateSkill(stub, FakeDns.Public("example.com"));

        var result = await Invoke(skill, "http://example.com/page");

        Assert.Equal("web_fetch_result", result.GetProperty("kind").GetString());
        var data = result.GetProperty("data");
        Assert.Equal(200, data.GetProperty("status").GetInt32());
        Assert.Equal("Hello, world.", data.GetProperty("text").GetString());
        Assert.False(data.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task Rejects_non_http_schemes()
    {
        var skill = CreateSkill(new StubHttpMessageHandler(), FakeDns.Public("example.com"));

        var result = await Invoke(skill, "file:///etc/passwd");

        Assert.Equal("error", result.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Rejects_relative_url()
    {
        var skill = CreateSkill(new StubHttpMessageHandler(), FakeDns.Public("example.com"));

        var result = await Invoke(skill, "/relative/path");

        Assert.Equal("error", result.GetProperty("kind").GetString());
    }

    [Theory]
    [InlineData("http://127.0.0.1/", "loopback")]
    [InlineData("http://10.0.0.5/", "RFC1918 10/8")]
    [InlineData("http://192.168.1.1/", "RFC1918 192.168/16")]
    [InlineData("http://172.20.5.1/", "RFC1918 172.16/12")]
    [InlineData("http://169.254.169.254/latest/meta-data/", "AWS metadata link-local")]
    [InlineData("http://[::1]/", "IPv6 loopback")]
    public async Task Rejects_private_ip_literal(string url, string _why)
    {
        var skill = CreateSkill(new StubHttpMessageHandler(), FakeDns.Empty());

        var result = await Invoke(skill, url);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        var msg = result.GetProperty("data").GetProperty("message").GetString() ?? string.Empty;
        Assert.Contains("private", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_dns_resolved_private_ip()
    {
        // Hostname looks public but resolves to a private address — classic
        // DNS rebinding / cloud-metadata pivot.
        var skill = CreateSkill(new StubHttpMessageHandler(), FakeDns.Of("evil.example.com", "127.0.0.1"));

        var result = await Invoke(skill, "http://evil.example.com/probe");

        Assert.Equal("error", result.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Rejects_non_text_content_type()
    {
        var stub = new StubHttpMessageHandler();
        stub.When(HttpMethod.Get, "/binary", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[] { 0x00, 0x01, 0x02 })
            {
                Headers = { ContentType = new MediaTypeHeaderValue("image/png") }
            }
        });

        var skill = CreateSkill(stub, FakeDns.Public("example.com"));

        var result = await Invoke(skill, "http://example.com/binary");

        Assert.Equal("error", result.GetProperty("kind").GetString());
        var msg = result.GetProperty("data").GetProperty("message").GetString() ?? string.Empty;
        Assert.Contains("image/png", msg);
    }

    [Fact]
    public async Task Truncates_oversized_responses_at_256_kb_and_marks_truncated()
    {
        var oversized = new string('A', 300 * 1024);
        var stub = new StubHttpMessageHandler();
        stub.When(HttpMethod.Get, "/big", _ => TextResponse(oversized, "text/plain"));

        var skill = CreateSkill(stub, FakeDns.Public("example.com"));

        var result = await Invoke(skill, "http://example.com/big");

        Assert.Equal("web_fetch_result", result.GetProperty("kind").GetString());
        var data = result.GetProperty("data");
        Assert.True(data.GetProperty("truncated").GetBoolean());
        var body = data.GetProperty("text").GetString() ?? string.Empty;
        Assert.Equal(256 * 1024, body.Length);
    }

    [Fact]
    public async Task Returns_non_2xx_status_with_body_so_agent_can_diagnose()
    {
        var stub = new StubHttpMessageHandler();
        stub.When(HttpMethod.Get, "/missing", _ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found", Encoding.UTF8, "text/plain")
            };
            return resp;
        });

        var skill = CreateSkill(stub, FakeDns.Public("example.com"));

        var result = await Invoke(skill, "http://example.com/missing");

        Assert.Equal("web_fetch_result", result.GetProperty("kind").GetString());
        Assert.Equal(404, result.GetProperty("data").GetProperty("status").GetInt32());
    }

    [Fact]
    public void IsBlockedAddress_recognizes_canonical_private_ranges()
    {
        Assert.True(WebFetchSkill.IsBlockedAddress(IPAddress.Loopback));                           // 127.0.0.1
        Assert.True(WebFetchSkill.IsBlockedAddress(IPAddress.IPv6Loopback));                       // ::1
        Assert.True(WebFetchSkill.IsBlockedAddress(IPAddress.Parse("10.0.0.1")));
        Assert.True(WebFetchSkill.IsBlockedAddress(IPAddress.Parse("172.20.5.4")));
        Assert.True(WebFetchSkill.IsBlockedAddress(IPAddress.Parse("192.168.5.5")));
        Assert.True(WebFetchSkill.IsBlockedAddress(IPAddress.Parse("169.254.169.254")));
        Assert.True(WebFetchSkill.IsBlockedAddress(IPAddress.Parse("224.0.0.1")));                 // multicast
        Assert.True(WebFetchSkill.IsBlockedAddress(IPAddress.Parse("fc00::1")));                   // ULA
        Assert.True(WebFetchSkill.IsBlockedAddress(IPAddress.Parse("fe80::1")));                   // link-local
        // Public addresses pass.
        Assert.False(WebFetchSkill.IsBlockedAddress(IPAddress.Parse("8.8.8.8")));
        Assert.False(WebFetchSkill.IsBlockedAddress(IPAddress.Parse("1.1.1.1")));
        Assert.False(WebFetchSkill.IsBlockedAddress(IPAddress.Parse("2606:4700:4700::1111")));     // Cloudflare DNS
    }

    private static WebFetchSkill CreateSkill(StubHttpMessageHandler stub, IDnsResolver dns)
    {
        var client = new HttpClient(stub);
        var factory = new SingleClientFactory(client);
        return new WebFetchSkill(factory, dns);
    }

    private static async Task<JsonElement> Invoke(WebFetchSkill skill, string url)
    {
        var args = JsonDocument.Parse(JsonSerializer.Serialize(new { url })).RootElement;
        var tool = skill.Tools.Single();
        var ctx = new AgentToolContext(
            new AgentSessionContext(new System.Security.Claims.ClaimsPrincipal(), Guid.Empty, "test"),
            new EmptyServiceProvider());
        return await tool.Invoke(args, ctx, CancellationToken.None);
    }

    private static HttpResponseMessage TextResponse(string body, string contentType) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, contentType)
    };

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingleClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    // FakeDns stand-in. Empty() throws if asked to resolve anything (used when
    // tests pass an IP literal — DNS shouldn't be touched). Of(host, ips)
    // returns the canned IPs. Public(host) returns 1.1.1.1 (public).
    private static class FakeDns
    {
        public static IDnsResolver Empty() => new Recorder(new Dictionary<string, IPAddress[]>(), strict: true);
        public static IDnsResolver Public(string host) => Of(host, "1.1.1.1");
        public static IDnsResolver Of(string host, params string[] ips) =>
            new Recorder(new Dictionary<string, IPAddress[]>(StringComparer.OrdinalIgnoreCase)
            {
                [host] = ips.Select(IPAddress.Parse).ToArray()
            }, strict: false);

        private sealed class Recorder : IDnsResolver
        {
            private readonly IReadOnlyDictionary<string, IPAddress[]> _table;
            private readonly bool _strict;
            public Recorder(IReadOnlyDictionary<string, IPAddress[]> table, bool strict)
            {
                _table = table;
                _strict = strict;
            }
            public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
            {
                if (_table.TryGetValue(host, out var ips)) return Task.FromResult(ips);
                if (_strict) throw new InvalidOperationException($"DNS unexpectedly queried for {host}");
                return Task.FromResult(new[] { IPAddress.Parse("1.1.1.1") });
            }
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
