using AutoNate.Web.Services.ExternalConnections;
using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Services.Agent.Catalog;

// Pulls the live model list from each provider that has at least one
// enabled External Connection and inserts any model ids the catalog hasn't
// seen yet. Existing rows are NEVER overwritten — admin edits to display
// name, cost, and description are preserved. Costs aren't returned by
// either provider's /v1/models endpoint, so refresh adds new ids with
// blank costs and the admin fills them in afterwards.
public interface IAgentModelCatalogRefresher
{
    Task<RefreshResult> RefreshAsync(CancellationToken cancellationToken = default);
}

public sealed record class RefreshResult(
    IReadOnlyList<RefreshedProvider> Providers,
    IReadOnlyList<string> SkippedReasons);

public sealed record class RefreshedProvider(
    string Provider,
    string ConnectionKind,
    Guid ConnectionId,
    int ProviderModelCount,
    IReadOnlyList<string> AddedModelIds,
    string? Error);

public sealed class AgentModelCatalogRefresher : IAgentModelCatalogRefresher
{
    // Maps the External Connection `kind` discriminator to the catalog
    // `provider` value. Refresh only walks kinds it knows how to bucket;
    // anything else (e.g. WebSearchProvider) is skipped with a reason.
    private static readonly IReadOnlyDictionary<string, string> KindToProvider = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["LlmProvider:Anthropic"] = "Anthropic",
        ["LlmProvider:OpenAI"] = "OpenAI"
    };

    private readonly IExternalConnectionStore _connectionStore;
    private readonly IConnectionModelLister _modelLister;
    private readonly IAgentModelCatalogStore _catalogStore;
    private readonly ILogger<AgentModelCatalogRefresher> _logger;

    public AgentModelCatalogRefresher(
        IExternalConnectionStore connectionStore,
        IConnectionModelLister modelLister,
        IAgentModelCatalogStore catalogStore,
        ILogger<AgentModelCatalogRefresher> logger)
    {
        _connectionStore = connectionStore;
        _modelLister = modelLister;
        _catalogStore = catalogStore;
        _logger = logger;
    }

    public async Task<RefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var providerResults = new List<RefreshedProvider>();
        var skipped = new List<string>();

        var connections = await _connectionStore.ListAsync(kind: null, cancellationToken);
        // One connection per provider is enough — pick the default if set,
        // otherwise the first enabled one. Connections without a stored
        // secret can't be polled, so we skip them with a reason.
        var byProvider = connections
            .Where(c => c.IsEnabled && KindToProvider.ContainsKey(c.Kind))
            .GroupBy(c => KindToProvider[c.Kind]);

        foreach (var group in byProvider)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var providerName = group.Key;
            var pick = group.FirstOrDefault(c => c.IsDefault) ?? group.First();
            if (string.IsNullOrEmpty(pick.SecretFingerprint))
            {
                skipped.Add($"{providerName}: connection '{pick.Name}' has no stored API key.");
                continue;
            }

            var secret = await RevealSecretAsync(pick.Id, cancellationToken);
            if (string.IsNullOrEmpty(secret))
            {
                skipped.Add($"{providerName}: connection '{pick.Name}' could not be decrypted.");
                continue;
            }
            var listing = await _modelLister.ListModelsAsync(
                new ListModelsInput(pick.Kind, BaseUrlFromMetadata(pick), secret),
                cancellationToken);
            if (!listing.Ok)
            {
                providerResults.Add(new RefreshedProvider(
                    Provider: providerName,
                    ConnectionKind: pick.Kind,
                    ConnectionId: pick.Id,
                    ProviderModelCount: 0,
                    AddedModelIds: Array.Empty<string>(),
                    Error: listing.Error ?? "Unknown error from provider."));
                continue;
            }

            // Use the existing list as a baseline for sort order; check
            // each upstream id with GetByModelIdAsync so we also dedupe
            // against any archived rows left over from when the archive
            // feature existed (those wouldn't appear in ListAsync anymore
            // and we'd otherwise trip the unique model_id constraint).
            var existing = await _catalogStore.ListAsync(providerName, cancellationToken);
            var added = new List<string>();
            var maxSort = existing.Count == 0 ? 0 : existing.Max(e => e.SortOrder);

            foreach (var model in listing.Models)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var alreadyKnown = await _catalogStore.GetByModelIdAsync(model.Id, cancellationToken);
                if (alreadyKnown is not null) continue;
                maxSort += 10;
                await _catalogStore.CreateAsync(new CreateAgentModelInput(
                    ModelId: model.Id,
                    DisplayName: model.Id,
                    Provider: providerName,
                    ContextWindowTokens: model.ContextWindowTokens > 0
                        ? model.ContextWindowTokens
                        : 100_000,
                    InputCostPerMillionTokens: null,
                    OutputCostPerMillionTokens: null,
                    CostCurrency: "USD",
                    CostPublishedAtUtc: null,
                    Description: $"Imported from {providerName} on {DateTime.UtcNow:yyyy-MM-dd}. Costs and description not provided by the API — fill these in manually.",
                    SortOrder: maxSort), cancellationToken);
                added.Add(model.Id);
            }

            providerResults.Add(new RefreshedProvider(
                Provider: providerName,
                ConnectionKind: pick.Kind,
                ConnectionId: pick.Id,
                ProviderModelCount: listing.Models.Count,
                AddedModelIds: added,
                Error: null));
        }

        if (providerResults.Count == 0 && skipped.Count == 0)
        {
            skipped.Add("No enabled LLM connections configured.");
        }
        return new RefreshResult(providerResults, skipped);
    }

    private static string? BaseUrlFromMetadata(ExternalConnectionRow row)
    {
        if (row.Metadata.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        if (!row.Metadata.TryGetProperty("baseUrl", out var prop)) return null;
        return prop.ValueKind == System.Text.Json.JsonValueKind.String ? prop.GetString() : null;
    }

    private async Task<string?> RevealSecretAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        var revealed = await _connectionStore.RevealForResolverAsync(connectionId, cancellationToken);
        return revealed?.Secret;
    }
}
