using System.Text.Json;
using AutoNate.Web.Services.Agent.Catalog;
using AutoNate.Web.Services.ExternalConnections;

namespace AutoNate.Web.Services.Agent.Providers;

public sealed class ChatProviderResolver : IChatProviderResolver
{
    // Maps the External Connection `kind` discriminator to the catalog
    // `provider` discriminator so a connection can ask the catalog for its
    // provider's default model.
    private static readonly IReadOnlyDictionary<string, string> KindToCatalogProvider = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["LlmProvider:Anthropic"] = "Anthropic",
        ["LlmProvider:OpenAI"] = "OpenAI"
    };

    private readonly IExternalConnectionStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAgentModelCatalog _catalog;
    private readonly AutoNate.Web.Services.ExternalConnections.IProviderBaseUrlPolicy _baseUrlPolicy;

    public ChatProviderResolver(
        IExternalConnectionStore store,
        IHttpClientFactory httpClientFactory,
        IAgentModelCatalog catalog,
        AutoNate.Web.Services.ExternalConnections.IProviderBaseUrlPolicy baseUrlPolicy)
    {
        _store = store;
        _httpClientFactory = httpClientFactory;
        _catalog = catalog;
        _baseUrlPolicy = baseUrlPolicy;
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
        // Connection's metadata.model wins when set (legacy / explicit
        // override); otherwise look up the catalog's default for this
        // provider; otherwise the hard-coded fallback so the loop has
        // *something* even on a brand-new install with an empty catalog.
        var modelId = TryReadString(revealed.Metadata, "model")
            ?? CatalogDefault(revealed.Kind)
            ?? DefaultModel(revealed.Kind);
        // The connection's baseUrl is operator-supplied metadata and the
        // provider puts the decrypted key on every request built from it, so
        // it is allowlisted here — at the boundary where untrusted metadata
        // becomes a destination — rather than in the provider (#61).
        var baseUrl = TryReadString(revealed.Metadata, "baseUrl");

        return revealed.Kind switch
        {
            "LlmProvider:Anthropic" => new AnthropicChatProvider(
                _httpClientFactory.CreateClient("agent.anthropic"),
                new AnthropicProviderOptions(
                    revealed.Secret, modelId,
                    _baseUrlPolicy.Resolve(revealed.Kind, baseUrl, "https://api.anthropic.com").ToString())),
            "LlmProvider:OpenAI" => new OpenAIChatProvider(
                _httpClientFactory.CreateClient("agent.openai"),
                new OpenAIProviderOptions(
                    revealed.Secret, modelId,
                    _baseUrlPolicy.Resolve(revealed.Kind, baseUrl, "https://api.openai.com").ToString())),
            _ => null
        };
    }

    private string? CatalogDefault(string kind)
    {
        if (!KindToCatalogProvider.TryGetValue(kind, out var providerName)) return null;

        // Single global default. If its provider matches the connection's
        // provider, use it — that's the model the chatbot was configured
        // around. If it doesn't match (e.g. the default is a Claude model
        // but this connection is OpenAI), fall back to the first available
        // model for the connection's provider so the chat still works.
        var globalDefault = _catalog.GetDefault();
        if (globalDefault is not null
            && string.Equals(globalDefault.Provider, providerName, StringComparison.OrdinalIgnoreCase))
        {
            return globalDefault.ModelId;
        }
        return _catalog.GetFirstAvailable(providerName)?.ModelId;
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
