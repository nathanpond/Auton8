using System.Text.Json;
using AutoNate.Web.Services.ExternalConnections;

namespace AutoNate.Web.Services.Agent.Providers;

public sealed class ChatProviderResolver : IChatProviderResolver
{
    private readonly IExternalConnectionStore _store;
    private readonly IHttpClientFactory _httpClientFactory;

    public ChatProviderResolver(
        IExternalConnectionStore store,
        IHttpClientFactory httpClientFactory)
    {
        _store = store;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IChatProvider?> ResolveAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var revealed = await _store.RevealForResolverAsync(connectionId, cancellationToken);
        if (revealed is null) return null;
        return Build(revealed);
    }

    public async Task<IChatProvider?> ResolveDefaultForKindAsync(string kind, CancellationToken cancellationToken = default)
    {
        var rows = await _store.ListAsync(kind, cancellationToken);
        var defaultRow = rows.FirstOrDefault(r => r.IsDefault && r.IsEnabled)
            ?? rows.FirstOrDefault(r => r.IsEnabled);
        if (defaultRow is null) return null;
        return await ResolveAsync(defaultRow.Id, cancellationToken);
    }

    private IChatProvider? Build(RevealedConnection revealed)
    {
        var modelId = TryReadString(revealed.Metadata, "model") ?? DefaultModel(revealed.Kind);
        var baseUrl = TryReadString(revealed.Metadata, "baseUrl");

        return revealed.Kind switch
        {
            "LlmProvider:Anthropic" => new AnthropicChatProvider(
                _httpClientFactory.CreateClient("agent.anthropic"),
                new AnthropicProviderOptions(revealed.Secret, modelId, baseUrl)),
            "LlmProvider:OpenAI" => new OpenAIChatProvider(
                _httpClientFactory.CreateClient("agent.openai"),
                new OpenAIProviderOptions(revealed.Secret, modelId, baseUrl)),
            _ => null
        };
    }

    private static string DefaultModel(string kind) => kind switch
    {
        "LlmProvider:Anthropic" => "claude-sonnet-4-6",
        "LlmProvider:OpenAI" => "gpt-4.1",
        _ => "unknown"
    };

    private static string? TryReadString(JsonElement metadata, string property)
    {
        if (metadata.ValueKind != JsonValueKind.Object) return null;
        if (!metadata.TryGetProperty(property, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }
}
