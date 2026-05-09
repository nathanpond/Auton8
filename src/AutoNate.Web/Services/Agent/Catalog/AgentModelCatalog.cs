using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Services.Agent.Catalog;

// Singleton in-memory snapshot of agent_model rows. Lazily initialised on
// first lookup; rebuilt when Invalidate() is called by the store. Thread
// safe because reads bind a single immutable snapshot reference; writes
// publish a new snapshot via interlocked exchange.
public sealed class AgentModelCatalog : IAgentModelCatalog
{
    public const int DefaultUnknownContextWindow = 100_000;

    private readonly IDbContextFactory<AutoNateDbContext> _dbContextFactory;
    private readonly ILogger<AgentModelCatalog> _logger;
    private readonly object _refreshLock = new();
    private Snapshot? _snapshot;

    public AgentModelCatalog(
        IDbContextFactory<AutoNateDbContext> dbContextFactory,
        ILogger<AgentModelCatalog> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public int UnknownModelContextWindow => DefaultUnknownContextWindow;

    public int GetContextWindow(string? modelId)
    {
        var snapshot = GetOrLoad();
        return ResolveContextWindow(snapshot.AllRows, modelId, UnknownModelContextWindow);
    }

    public bool IsKnown(string? modelId)
    {
        var snapshot = GetOrLoad();
        return ResolveIsKnown(snapshot.AllRows, modelId);
    }

    // Pure lookup helper. Exposed static so unit tests can exercise the
    // longest-prefix-match logic without spinning up a DbContext factory.
    public static int ResolveContextWindow(IReadOnlyList<AgentModelRow> rows, string? modelId, int fallback)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return fallback;

        // Exact match wins.
        foreach (var row in rows)
        {
            if (string.Equals(row.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
            {
                return row.ContextWindowTokens;
            }
        }

        // Longest-prefix match so versioned variants like
        // "claude-sonnet-4-6-20250514" resolve through "claude-sonnet-4-6".
        var bestLen = 0;
        var bestWindow = fallback;
        foreach (var row in rows)
        {
            if (modelId.StartsWith(row.ModelId, StringComparison.OrdinalIgnoreCase) && row.ModelId.Length > bestLen)
            {
                bestLen = row.ModelId.Length;
                bestWindow = row.ContextWindowTokens;
            }
        }
        return bestLen == 0 ? fallback : bestWindow;
    }

    public static bool ResolveIsKnown(IReadOnlyList<AgentModelRow> rows, string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return false;
        foreach (var row in rows)
        {
            if (string.Equals(row.ModelId, modelId, StringComparison.OrdinalIgnoreCase)) return true;
            if (modelId.StartsWith(row.ModelId, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public IReadOnlyList<AgentModelRow> All() => GetOrLoad().AllRows;

    public AgentModelRow? GetDefault()
    {
        var snapshot = GetOrLoad();
        foreach (var row in snapshot.AllRows)
        {
            if (row.IsDefault) return row;
        }
        return null;
    }

    public AgentModelRow? GetFirstAvailable(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider)) return null;
        var snapshot = GetOrLoad();
        foreach (var row in snapshot.AllRows)
        {
            if (!string.Equals(row.Provider, provider, StringComparison.OrdinalIgnoreCase)) continue;
            if (row.IsAvailable) return row;
        }
        return null;
    }

    public void Invalidate()
    {
        Volatile.Write(ref _snapshot, null);
    }

    private Snapshot GetOrLoad()
    {
        var current = Volatile.Read(ref _snapshot);
        if (current is not null) return current;

        lock (_refreshLock)
        {
            current = Volatile.Read(ref _snapshot);
            if (current is not null) return current;

            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                var rows = dbContext.AgentModels
                    .AsNoTracking()
                    .Where(m => !m.IsArchived)
                    .OrderBy(m => m.Provider)
                    .ThenBy(m => m.SortOrder)
                    .ThenBy(m => m.ModelId)
                    .ToList();
                var asRows = rows.Select(ToRow).ToList();
                var byModelId = asRows.ToDictionary(r => r.ModelId, StringComparer.OrdinalIgnoreCase);
                current = new Snapshot(asRows, byModelId);
            }
            catch (Exception ex)
            {
                // First-boot races (schema not yet created in tests) or DB
                // outages should not crash the chat hot path. Log and serve
                // an empty snapshot — GetContextWindow falls back to the
                // conservative default for every id, which means the trimmer
                // is just more aggressive than usual.
                _logger.LogWarning(ex, "Failed to load agent_model snapshot; serving empty catalog.");
                current = new Snapshot(Array.Empty<AgentModelRow>(), new Dictionary<string, AgentModelRow>(StringComparer.OrdinalIgnoreCase));
            }

            Volatile.Write(ref _snapshot, current);
            return current;
        }
    }

    private static AgentModelRow ToRow(AgentModel entity) => new(
        Id: entity.Id,
        ModelId: entity.ModelId,
        DisplayName: entity.DisplayName,
        Provider: entity.Provider,
        ContextWindowTokens: entity.ContextWindowTokens,
        InputCostPerMillionTokens: entity.InputCostPerMillionTokens,
        OutputCostPerMillionTokens: entity.OutputCostPerMillionTokens,
        CostCurrency: entity.CostCurrency,
        CostPublishedAtUtc: entity.CostPublishedAtUtc,
        Description: entity.Description,
        IsArchived: entity.IsArchived,
        IsDefault: entity.IsDefault,
        IsAvailable: entity.IsAvailable,
        SortOrder: entity.SortOrder,
        CreatedAtUtc: entity.CreatedAtUtc,
        UpdatedAtUtc: entity.UpdatedAtUtc);

    private sealed record class Snapshot(
        IReadOnlyList<AgentModelRow> AllRows,
        IReadOnlyDictionary<string, AgentModelRow> ByModelId);
}
