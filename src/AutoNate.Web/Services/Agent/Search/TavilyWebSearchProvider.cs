using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoNate.Web.Services.Agent.Search;

// Tavily Search API. Wire format:
//   POST {base}/search
//   Authorization: Bearer tvly-…
//   { "query": "...", "max_results": 5, "search_depth": "basic",
//     "include_answer": false, "include_raw_content": false }
// Response:
//   { "query": "...", "results":
//     [{ "title", "url", "content", "score", "published_date"? }, ...] }
//
// We intentionally don't request raw_content — the agent already has fetch_url
// for full-page bodies. Snippets are clamped to 1 KB to keep the tool result
// size reasonable in the model's context.
public sealed class TavilyWebSearchProvider : IWebSearchProvider
{
    private const int SnippetMaxLength = 1024;

    public string Kind => "Tavily";

    private readonly HttpClient _http;
    private readonly TavilyProviderOptions _options;

    public TavilyWebSearchProvider(HttpClient http, TavilyProviderOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default)
    {
        var baseUrl = new Uri(string.IsNullOrWhiteSpace(_options.BaseUrl) ? "https://api.tavily.com" : _options.BaseUrl);
        var maxResults = Math.Clamp(request.MaxResults <= 0 ? 5 : request.MaxResults, 1, 10);

        var body = BuildRequestBody(request.Query, maxResults);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUrl, "/search"))
        {
            Content = JsonContent.Create(body)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Tavily returned {(int)response.StatusCode}: {Truncate(raw, 256)}");
        }

        return ParseResponse(raw, request.Query);
    }

    // Public-static so tests can assert request shape without calling SendAsync.
    public static JsonObject BuildRequestBody(string query, int maxResults) => new()
    {
        ["query"] = query,
        ["max_results"] = maxResults,
        ["search_depth"] = "basic",
        ["include_answer"] = false,
        ["include_raw_content"] = false
    };

    public static WebSearchResponse ParseResponse(string raw, string fallbackQuery)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new WebSearchResponse("Tavily", fallbackQuery, Array.Empty<WebSearchHit>());
        }

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var query = root.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String
            ? q.GetString() ?? fallbackQuery
            : fallbackQuery;

        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return new WebSearchResponse("Tavily", query, Array.Empty<WebSearchHit>());
        }

        var hits = new List<WebSearchHit>();
        foreach (var element in results.EnumerateArray())
        {
            var title = ReadString(element, "title") ?? string.Empty;
            var url = ReadString(element, "url") ?? string.Empty;
            var snippet = ReadString(element, "content") ?? string.Empty;
            if (snippet.Length > SnippetMaxLength) snippet = snippet[..SnippetMaxLength] + "…";
            double? score = element.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetDouble()
                : null;
            DateTime? published = null;
            if (element.TryGetProperty("published_date", out var p) && p.ValueKind == JsonValueKind.String
                && DateTime.TryParse(p.GetString(), out var parsed))
            {
                published = parsed.ToUniversalTime();
            }

            if (!string.IsNullOrWhiteSpace(url))
            {
                hits.Add(new WebSearchHit(title, url, snippet, score, published));
            }
        }

        return new WebSearchResponse("Tavily", query, hits);
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

public sealed record class TavilyProviderOptions(string ApiKey, string? BaseUrl);
