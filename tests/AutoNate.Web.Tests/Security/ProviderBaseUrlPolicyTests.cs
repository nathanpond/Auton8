using AutoNate.Web.Services.ExternalConnections;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoNate.Web.Tests.Security;

// archived-61: an external connection's `baseUrl` is operator-supplied metadata that
// ends up on every request carrying the decrypted provider key, so pointing a
// connection at an attacker's host handed them the credential.
public sealed class ProviderBaseUrlPolicyTests
{
    private const string Anthropic = "LlmProvider:Anthropic";
    private const string OpenAI = "LlmProvider:OpenAI";
    private const string Tavily = "WebSearchProvider:Tavily";

    [Theory]
    [InlineData(Anthropic, "https://api.anthropic.com")]
    [InlineData(OpenAI, "https://api.openai.com")]
    [InlineData(Tavily, "https://api.tavily.com")]
    public void No_override_uses_the_built_in_default(string kind, string expected)
    {
        var policy = Policy();

        foreach (var candidate in new[] { null, "", "   " })
        {
            var uri = policy.Resolve(kind, candidate, expected);
            Assert.Equal(new Uri(expected), uri);
        }
    }

    [Theory]
    [InlineData(Anthropic, "https://api.anthropic.com")]
    [InlineData(OpenAI, "https://api.openai.com")]
    [InlineData(Tavily, "https://api.tavily.com")]
    public void The_official_host_is_allowed_explicitly(string kind, string url)
    {
        var uri = Policy().Resolve(kind, url, url);

        Assert.Equal(new Uri(url), uri);
    }

    [Theory]
    [InlineData("https://attacker.example")]
    [InlineData("https://api.anthropic.com.attacker.example")]   // suffix confusion
    [InlineData("https://169.254.169.254")]
    [InlineData("https://localhost")]
    public void A_host_outside_the_allowlist_is_refused(string url)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Policy().Resolve(OpenAI, url, "https://api.openai.com"));

        Assert.Contains("is not an allowed endpoint", ex.Message);
        // The message has to tell the operator how to permit a legitimate one.
        Assert.Contains("ExternalConnections:AllowedProviderHosts", ex.Message);
    }

    [Fact]
    public void Plain_http_is_refused_even_for_an_allowlisted_host()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Policy().Resolve(OpenAI, "http://api.openai.com", "https://api.openai.com"));

        Assert.Contains("must use https", ex.Message);
    }

    [Fact]
    public void A_non_absolute_url_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Policy().Resolve(OpenAI, "not a url", "https://api.openai.com"));

        Assert.Contains("not an absolute URI", ex.Message);
    }

    [Fact]
    public void An_operator_configured_host_is_allowed()
    {
        var policy = Policy(new Dictionary<string, string[]>
        {
            [OpenAI] = ["my-gateway.corp.example"],
        });

        var uri = policy.Resolve(OpenAI, "https://my-gateway.corp.example/v1", "https://api.openai.com");

        Assert.Equal("my-gateway.corp.example", uri.Host);
        // Configuring one kind must not open another.
        Assert.Throws<InvalidOperationException>(
            () => policy.Resolve(Anthropic, "https://my-gateway.corp.example", "https://api.anthropic.com"));
    }

    [Fact]
    public void A_configured_wildcard_matches_subdomains_but_not_the_bare_suffix()
    {
        var policy = Policy(new Dictionary<string, string[]>
        {
            [OpenAI] = ["*.azure-api.net"],
        });

        Assert.Equal(
            "acme.azure-api.net",
            policy.Resolve(OpenAI, "https://acme.azure-api.net", "https://api.openai.com").Host);
        Assert.Throws<InvalidOperationException>(
            () => policy.Resolve(OpenAI, "https://azure-api.net", "https://api.openai.com"));
        Assert.Throws<InvalidOperationException>(
            () => policy.Resolve(OpenAI, "https://notazure-api.net", "https://api.openai.com"));
    }

    // A kind with no built-in entry has an empty allowlist: an override must be
    // configured deliberately rather than defaulting to "anything goes".
    [Fact]
    public void An_unknown_kind_refuses_every_override()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Policy().Resolve("LlmProvider:Unknown", "https://whatever.example", "https://api.openai.com"));

        Assert.Contains("is not an allowed endpoint", ex.Message);
    }

    [Fact]
    public void Host_matching_is_case_insensitive()
    {
        var uri = Policy().Resolve(OpenAI, "https://API.OpenAI.CoM/v1", "https://api.openai.com");

        Assert.Equal("api.openai.com", uri.Host);
    }

    private static ProviderBaseUrlPolicy Policy(Dictionary<string, string[]>? configured = null) =>
        new(Options.Create(new ExternalConnectionUrlOptions
        {
            AllowedProviderHosts = configured is null
                ? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string[]>(configured, StringComparer.OrdinalIgnoreCase),
        }));
}
