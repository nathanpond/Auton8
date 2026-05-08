using System.Text.Json;
using AutoNate.Web.Services.Agent.Search;
using AutoNate.Web.Services.Agent.Skills;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class WebSearchSkillTests
{
    [Fact]
    public async Task Returns_results_envelope_when_provider_succeeds()
    {
        var fakeProvider = new FakeProvider(new WebSearchResponse(
            "FakeProvider",
            "node lts",
            new[]
            {
                new WebSearchHit("Node.js LTS", "https://nodejs.org/lts", "Node.js LTS info", 0.92, null)
            }));
        var skill = new WebSearchSkill(new FakeResolver(fakeProvider));

        var result = await Invoke(skill, new { query = "node lts", max_results = 3 });

        Assert.Equal("web_search_results", result.GetProperty("kind").GetString());
        var data = result.GetProperty("data");
        Assert.Equal("FakeProvider", data.GetProperty("provider").GetString());
        Assert.Equal("node lts", data.GetProperty("query").GetString());
        var hits = data.GetProperty("results");
        Assert.Equal(1, hits.GetArrayLength());
        Assert.Equal("https://nodejs.org/lts", hits[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Returns_error_envelope_when_no_provider_is_configured()
    {
        var skill = new WebSearchSkill(new FakeResolver(provider: null));

        var result = await Invoke(skill, new { query = "anything" });

        Assert.Equal("error", result.GetProperty("kind").GetString());
        var msg = result.GetProperty("data").GetProperty("message").GetString() ?? string.Empty;
        Assert.Contains("No web search provider", msg);
        Assert.Contains("External Connections", msg);
    }

    [Fact]
    public async Task Returns_error_envelope_when_provider_throws()
    {
        var skill = new WebSearchSkill(new FakeResolver(new ThrowingProvider("upstream blew up")));

        var result = await Invoke(skill, new { query = "anything" });

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Contains("upstream blew up",
            result.GetProperty("data").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Rejects_missing_query()
    {
        var skill = new WebSearchSkill(new FakeResolver(new ThrowingProvider("nope")));

        var result = await Invoke(skill, new { max_results = 5 });

        Assert.Equal("error", result.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Rejects_empty_query()
    {
        var skill = new WebSearchSkill(new FakeResolver(new ThrowingProvider("nope")));

        var result = await Invoke(skill, new { query = "   " });

        Assert.Equal("error", result.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Clamps_max_results_into_1_to_10()
    {
        var fake = new FakeProvider(new WebSearchResponse("F", "q", Array.Empty<WebSearchHit>()));
        var skill = new WebSearchSkill(new FakeResolver(fake));

        await Invoke(skill, new { query = "q", max_results = 999 });

        Assert.Equal(10, fake.LastRequest!.MaxResults);

        await Invoke(skill, new { query = "q", max_results = -3 });

        Assert.Equal(1, fake.LastRequest.MaxResults);
    }

    private static async Task<JsonElement> Invoke(WebSearchSkill skill, object args)
    {
        var argsJson = JsonSerializer.Serialize(args);
        using var doc = JsonDocument.Parse(argsJson);
        var tool = skill.Tools.Single();
        var ctx = new AgentToolContext(
            new AgentSessionContext(new System.Security.Claims.ClaimsPrincipal(), Guid.Empty, "test"),
            new EmptyServiceProvider());
        return await tool.Invoke(doc.RootElement, ctx, CancellationToken.None);
    }

    private sealed class FakeResolver : IWebSearchProviderResolver
    {
        private readonly IWebSearchProvider? _provider;
        public FakeResolver(IWebSearchProvider? provider) => _provider = provider;
        public Task<IWebSearchProvider?> ResolveAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_provider);
        public Task<IWebSearchProvider?> ResolveDefaultAsync(CancellationToken ct = default) =>
            Task.FromResult(_provider);
    }

    private sealed class FakeProvider : IWebSearchProvider
    {
        private readonly WebSearchResponse _canned;
        public FakeProvider(WebSearchResponse canned) => _canned = canned;
        public string Kind => "Fake";
        public WebSearchRequest? LastRequest { get; private set; }
        public Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_canned);
        }
    }

    private sealed class ThrowingProvider : IWebSearchProvider
    {
        private readonly string _message;
        public ThrowingProvider(string message) => _message = message;
        public string Kind => "Throwing";
        public Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(_message);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
