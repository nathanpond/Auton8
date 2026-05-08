namespace AutoNate.Web.Services.Agent.Search;

// Provider-neutral web-search abstraction. Mirrors the shape of IChatProvider:
// the resolver constructs one of these per request scope after loading the
// External Connection row and decrypting its api key. The skill then calls
// SearchAsync; everything provider-specific (Tavily payload shape, Brave
// param names, etc.) stays inside the implementation.
public interface IWebSearchProvider
{
    string Kind { get; }

    Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default);
}

public sealed record class WebSearchRequest(string Query, int MaxResults);

public sealed record class WebSearchResponse(
    string Provider,
    string Query,
    IReadOnlyList<WebSearchHit> Results);

public sealed record class WebSearchHit(
    string Title,
    string Url,
    string Snippet,
    double? Score,
    DateTime? PublishedAtUtc);
