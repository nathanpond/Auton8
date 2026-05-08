using System.Text.Json;
using AutoNate.Web.Services.Agent.Search;

namespace AutoNate.Web.Services.Agent.Skills;

// Optional skill — only registered when the admin has enabled internet
// access in Chatbot Settings. The agent loop's gating filter drops
// `web_search` from the offered tools when the toggle is off, in lockstep
// with `fetch_url`.
//
// The skill never reaches out to a search API directly — it asks the
// resolver for the configured provider (Tavily today, more later) and
// delegates. If no provider is configured, the skill returns a friendly
// error envelope instead of crashing; the model surfaces that to the user.
public sealed class WebSearchSkill : IAgentSkill
{
    public const string ToolName = "web_search";

    private readonly IWebSearchProviderResolver _resolver;

    public WebSearchSkill(IWebSearchProviderResolver resolver)
    {
        _resolver = resolver;
        Tools = new[]
        {
            new AgentTool(
                Name: ToolName,
                Description: "Search the public web for a query and return the top results (title, url, snippet). Use the URLs with fetch_url to read the full pages.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "query": { "type": "string", "description": "Plain-text search query." },
                        "max_results": { "type": "integer", "minimum": 1, "maximum": 10, "description": "Max results to return (1-10). Default 5." }
                      },
                      "required": ["query"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeAsync)
        };
    }

    public string Name => "web-search";

    public string Description => "Search the public web via the configured search provider.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "When you need fresh information you can't infer, call web_search first to find relevant URLs, then call fetch_url on the most relevant result. Search snippets are previews — fetch the page if details matter.";

    private async Task<JsonElement> InvokeAsync(JsonElement args, AgentToolContext context, CancellationToken cancellationToken)
    {
        if (!args.TryGetProperty("query", out var queryProp) || queryProp.ValueKind != JsonValueKind.String)
        {
            return Error("query is required.");
        }
        var query = (queryProp.GetString() ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            return Error("query must not be empty.");
        }

        var maxResults = 5;
        if (args.TryGetProperty("max_results", out var mr) && mr.ValueKind == JsonValueKind.Number && mr.TryGetInt32(out var asked))
        {
            maxResults = Math.Clamp(asked, 1, 10);
        }

        var provider = await _resolver.ResolveDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (provider is null)
        {
            return Error("No web search provider is configured. An admin can add one in Sitewide Configuration → External Connections (e.g. Tavily).");
        }

        WebSearchResponse response;
        try
        {
            response = await provider.SearchAsync(new WebSearchRequest(query, maxResults), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Error("Search was cancelled.");
        }
        catch (Exception ex)
        {
            return Error($"Search failed: {ex.Message}");
        }

        return JsonSerializer.SerializeToElement(new
        {
            kind = "web_search_results",
            source = "WebSearchSkill",
            data = new
            {
                provider = response.Provider,
                query = response.Query,
                results = response.Results.Select(r => new
                {
                    title = r.Title,
                    url = r.Url,
                    snippet = r.Snippet,
                    score = r.Score,
                    publishedAtUtc = r.PublishedAtUtc
                }).ToArray()
            }
        });
    }

    private static JsonElement Error(string message) =>
        JsonSerializer.SerializeToElement(new
        {
            kind = "error",
            source = ToolName,
            data = new { message }
        });

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
