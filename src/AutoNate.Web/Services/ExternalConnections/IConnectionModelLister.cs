using System.Net.Http.Headers;
using System.Text.Json;
using AutoNate.Web.Services.Agent.Catalog;

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

public sealed record class ListModelsResult(bool Ok, IReadOnlyList<ModelInfo> Models, string? Error);

// Context window comes from ModelCatalog (longest-prefix match against the
// id). KnownContextWindow is false when the catalog had to fall back to the
// conservative default — the admin UI surfaces that as a warning so the
// admin knows to override the value.
public sealed record class ModelInfo(string Id, int ContextWindowTokens, bool KnownContextWindow);

public sealed class ConnectionModelLister : IConnectionModelLister
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAgentModelCatalog _catalog;
    private readonly IProviderBaseUrlPolicy _baseUrlPolicy;

    public ConnectionModelLister(
        IHttpClientFactory httpClientFactory,
        IAgentModelCatalog catalog,
        IProviderBaseUrlPolicy baseUrlPolicy)
    {
        _httpClientFactory = httpClientFactory;
        _catalog = catalog;
        _baseUrlPolicy = baseUrlPolicy;
    }

    public async Task<ListModelsResult> ListModelsAsync(ListModelsInput input, CancellationToken cancellationToken = default)
    {
        try
        {
            return input.Kind switch
            {
                "LlmProvider:Anthropic" => await ListAnthropicAsync(input, cancellationToken),
                "LlmProvider:OpenAI" => await ListOpenAIAsync(input, cancellationToken),
                _ => new ListModelsResult(false, Array.Empty<ModelInfo>(), $"Listing models is not supported for kind '{input.Kind}'.")
            };
        }
        catch (Exception ex)
        {
            return new ListModelsResult(false, Array.Empty<ModelInfo>(), ex.Message);
        }
    }

    private async Task<ListModelsResult> ListAnthropicAsync(ListModelsInput input, CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient("agent.anthropic");
        // Allowlisted before the key is attached (#61).
        var baseUrl = _baseUrlPolicy.Resolve(input.Kind, input.BaseUrl, "https://api.anthropic.com");

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
                return new ListModelsResult(false, Array.Empty<ModelInfo>(), $"{(int)resp.StatusCode}: {Truncate(text, 256)}");
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

        return new ListModelsResult(true, ToInfos(ids), null);
    }

    private async Task<ListModelsResult> ListOpenAIAsync(ListModelsInput input, CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient("agent.openai");
        // Allowlisted before the key is attached (#61).
        var baseUrl = _baseUrlPolicy.Resolve(input.Kind, input.BaseUrl, "https://api.openai.com");

        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUrl, "/v1/models"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", input.Secret);

        using var resp = await http.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync(cancellationToken);
            return new ListModelsResult(false, Array.Empty<ModelInfo>(), $"{(int)resp.StatusCode}: {Truncate(text, 256)}");
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
        return new ListModelsResult(true, ToInfos(ids), null);
    }

    private IReadOnlyList<ModelInfo> ToInfos(IReadOnlyList<string> ids)
    {
        var infos = new ModelInfo[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            infos[i] = new ModelInfo(id, _catalog.GetContextWindow(id), _catalog.IsKnown(id));
        }
        return infos;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
