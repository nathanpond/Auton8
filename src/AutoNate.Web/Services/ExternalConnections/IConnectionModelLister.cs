using System.Net.Http.Headers;
using System.Text.Json;

namespace AutoNate.Web.Services.ExternalConnections;

// Queries a provider's /v1/models endpoint with the supplied credential and
// returns the model identifiers. Used by the External Connections admin
// page to populate the "model" dropdown so admins don't have to know the
// exact ids by hand.
public interface IConnectionModelLister
{
    Task<ListModelsResult> ListModelsAsync(ListModelsInput input, CancellationToken cancellationToken = default);
}

public sealed record class ListModelsInput(string Kind, string? BaseUrl, string Secret);

public sealed record class ListModelsResult(bool Ok, IReadOnlyList<string> Models, string? Error);

public sealed class ConnectionModelLister : IConnectionModelLister
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ConnectionModelLister(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ListModelsResult> ListModelsAsync(ListModelsInput input, CancellationToken cancellationToken = default)
    {
        try
        {
            return input.Kind switch
            {
                "LlmProvider:Anthropic" => await ListAnthropicAsync(input, cancellationToken),
                "LlmProvider:OpenAI" => await ListOpenAIAsync(input, cancellationToken),
                _ => new ListModelsResult(false, Array.Empty<string>(), $"Listing models is not supported for kind '{input.Kind}'.")
            };
        }
        catch (Exception ex)
        {
            return new ListModelsResult(false, Array.Empty<string>(), ex.Message);
        }
    }

    private async Task<ListModelsResult> ListAnthropicAsync(ListModelsInput input, CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient("agent.anthropic");
        var baseUrl = new Uri(string.IsNullOrWhiteSpace(input.BaseUrl) ? "https://api.anthropic.com" : input.BaseUrl);

        // Anthropic paginates with `after_id`; max page size 1000. A single
        // page is enough in practice (today's catalog is well under 100), but
        // we follow the cursor anyway in case it grows.
        var ids = new List<string>();
        string? afterId = null;
        for (var i = 0; i < 10; i++)
        {
            var url = new Uri(baseUrl, "/v1/models?limit=1000" + (afterId is null ? string.Empty : $"&after_id={afterId}"));
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("x-api-key", input.Secret);
            req.Headers.Add("anthropic-version", "2023-06-01");

            using var resp = await http.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                var text = await resp.Content.ReadAsStringAsync(cancellationToken);
                return new ListModelsResult(false, Array.Empty<string>(), $"{(int)resp.StatusCode}: {Truncate(text, 256)}");
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                break;
            }
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                {
                    var id = idProp.GetString();
                    if (!string.IsNullOrEmpty(id)) ids.Add(id);
                }
            }
            var hasMore = doc.RootElement.TryGetProperty("has_more", out var hm) && hm.ValueKind == JsonValueKind.True;
            if (!hasMore) break;
            afterId = doc.RootElement.TryGetProperty("last_id", out var li) && li.ValueKind == JsonValueKind.String
                ? li.GetString()
                : null;
            if (string.IsNullOrEmpty(afterId)) break;
        }

        return new ListModelsResult(true, ids, null);
    }

    private async Task<ListModelsResult> ListOpenAIAsync(ListModelsInput input, CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient("agent.openai");
        var baseUrl = new Uri(string.IsNullOrWhiteSpace(input.BaseUrl) ? "https://api.openai.com" : input.BaseUrl);

        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUrl, "/v1/models"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", input.Secret);

        using var resp = await http.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync(cancellationToken);
            return new ListModelsResult(false, Array.Empty<string>(), $"{(int)resp.StatusCode}: {Truncate(text, 256)}");
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var ids = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                {
                    var id = idProp.GetString();
                    if (!string.IsNullOrEmpty(id)) ids.Add(id);
                }
            }
        }
        return new ListModelsResult(true, ids, null);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
