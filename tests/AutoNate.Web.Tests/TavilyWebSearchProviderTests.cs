using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AutoNate.Web.Services.Agent.Search;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class TavilyWebSearchProviderTests
{
    [Fact]
    public void BuildRequestBody_emits_the_documented_tavily_search_shape()
    {
        var body = TavilyWebSearchProvider.BuildRequestBody("latest node.js LTS", 7);

        Assert.Equal("latest node.js LTS", body["query"]!.GetValue<string>());
        Assert.Equal(7, body["max_results"]!.GetValue<int>());
        Assert.Equal("basic", body["search_depth"]!.GetValue<string>());
        Assert.False(body["include_answer"]!.GetValue<bool>());
        Assert.False(body["include_raw_content"]!.GetValue<bool>());
    }

    [Fact]
    public void ParseResponse_maps_results_and_truncates_oversized_snippets()
    {
        var oversized = new string('A', 2000);
        var raw = $$"""
            {
              "query": "test",
              "results": [
                {"title":"First","url":"https://example.com/1","content":"hello","score":0.95,"published_date":"2026-01-15T00:00:00Z"},
                {"title":"Big","url":"https://example.com/2","content":"{{oversized}}","score":0.5}
              ]
            }
            """;

        var response = TavilyWebSearchProvider.ParseResponse(raw, fallbackQuery: "fallback");

        Assert.Equal("Tavily", response.Provider);
        Assert.Equal("test", response.Query);
        Assert.Equal(2, response.Results.Count);

        Assert.Equal("First", response.Results[0].Title);
        Assert.Equal("https://example.com/1", response.Results[0].Url);
        Assert.Equal("hello", response.Results[0].Snippet);
        Assert.Equal(0.95, response.Results[0].Score);
        Assert.Equal(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), response.Results[0].PublishedAtUtc);

        // Truncated to 1 KB + ellipsis; original was 2000 chars.
        Assert.True(response.Results[1].Snippet.Length <= 1024 + 1, $"got {response.Results[1].Snippet.Length}");
        Assert.EndsWith("…", response.Results[1].Snippet);
    }

    [Fact]
    public void ParseResponse_skips_results_without_a_url()
    {
        var raw = """
            {
              "query": "x",
              "results": [
                {"title":"NoUrl","content":"snippet"},
                {"title":"OK","url":"https://example.com","content":"snippet"}
              ]
            }
            """;

        var response = TavilyWebSearchProvider.ParseResponse(raw, fallbackQuery: "x");

        var hit = Assert.Single(response.Results);
        Assert.Equal("https://example.com", hit.Url);
    }

    [Fact]
    public async Task SearchAsync_sends_bearer_token_and_returns_parsed_results()
    {
        var stub = new StubHttpMessageHandler();
        stub.When(HttpMethod.Post, "/search", req =>
        {
            // Capture the auth header for assertion via the request log;
            // also verify it's set here so the test fails on the right line.
            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            Assert.Equal("tvly-test-key", req.Headers.Authorization?.Parameter);

            var body = new
            {
                query = "ok",
                results = new[]
                {
                    new { title = "One", url = "https://example.com/1", content = "first", score = 0.9 }
                }
            };
            return StubHttpMessageHandler.JsonResponse(body);
        });

        var provider = new TavilyWebSearchProvider(
            new HttpClient(stub) { BaseAddress = new Uri("https://api.tavily.com") },
            new TavilyProviderOptions("tvly-test-key", BaseUrl: null));

        var response = await provider.SearchAsync(new WebSearchRequest("ok", 5));

        Assert.Equal("Tavily", response.Provider);
        Assert.Single(response.Results);
        Assert.Equal("https://example.com/1", response.Results[0].Url);
    }

    [Fact]
    public async Task SearchAsync_throws_on_non_2xx_with_truncated_body()
    {
        var stub = new StubHttpMessageHandler();
        stub.When(HttpMethod.Post, "/search", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("invalid api key", Encoding.UTF8, "application/json")
        });

        var provider = new TavilyWebSearchProvider(
            new HttpClient(stub),
            new TavilyProviderOptions("tvly-bad", BaseUrl: null));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SearchAsync(new WebSearchRequest("ok", 5)));
        Assert.Contains("401", ex.Message);
        Assert.Contains("invalid api key", ex.Message);
    }
}
