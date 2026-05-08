using AutoNate.Web.Services.ExternalConnections;

namespace AutoNate.Web.Services.Agent.Search;

public sealed class WebSearchProviderResolver : IWebSearchProviderResolver
{
    // Connections of any kind starting with this prefix are considered web-
    // search providers. Adding Brave or Serper later: register a new kind
    // string and a new branch in Build below.
    public const string KindPrefix = "WebSearchProvider:";

    public const string KindTavily = "WebSearchProvider:Tavily";

    private readonly IExternalConnectionStore _store;
    private readonly IHttpClientFactory _httpClientFactory;

    public WebSearchProviderResolver(
        IExternalConnectionStore store,
        IHttpClientFactory httpClientFactory)
    {
        _store = store;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IWebSearchProvider?> ResolveAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var revealed = await _store.RevealForResolverAsync(connectionId, cancellationToken);
        if (revealed is null) return null;
        return Build(revealed);
    }

    public async Task<IWebSearchProvider?> ResolveDefaultAsync(CancellationToken cancellationToken = default)
    {
        // Without a "kind starts with" filter on the store, list each known
        // kind and merge. Cheap — there are typically zero or one rows per
        // kind. When a second provider lands, append its kind to the array.
        var rows = (await _store.ListAsync(KindTavily, cancellationToken)).ToList();
        var preferred = rows.FirstOrDefault(r => r.IsDefault && r.IsEnabled)
            ?? rows.FirstOrDefault(r => r.IsEnabled);
        if (preferred is null) return null;
        return await ResolveAsync(preferred.Id, cancellationToken);
    }

    private IWebSearchProvider? Build(RevealedConnection revealed)
    {
        if (!revealed.Kind.StartsWith(KindPrefix, StringComparison.Ordinal)) return null;

        var baseUrl = TryReadString(revealed.Metadata, "baseUrl");

        return revealed.Kind switch
        {
            KindTavily => new TavilyWebSearchProvider(
                _httpClientFactory.CreateClient("agent.websearch"),
                new TavilyProviderOptions(revealed.Secret, baseUrl)),
            _ => null
        };
    }

    private static string? TryReadString(System.Text.Json.JsonElement metadata, string property)
    {
        if (metadata.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        if (!metadata.TryGetProperty(property, out var prop)) return null;
        return prop.ValueKind == System.Text.Json.JsonValueKind.String ? prop.GetString() : null;
    }
}
