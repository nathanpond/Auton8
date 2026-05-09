namespace AutoNate.Web.Services.Agent.Catalog;

// CRUD over the agent_model table. Lookups in the chat hot path go through
// IAgentModelCatalog (in-memory cache) instead of this interface — direct
// store calls are admin-side only.
public interface IAgentModelCatalogStore
{
    // Lists non-archived rows. The is_archived column stays in the schema
    // for defensive forward-compat (older deployments may have rows with
    // it set from when the archive feature existed) but is no longer
    // writable through any public surface, so this filter is effectively
    // a no-op for fresh installs.
    Task<IReadOnlyList<AgentModelRow>> ListAsync(
        string? provider = null,
        CancellationToken cancellationToken = default);

    Task<AgentModelRow?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<AgentModelRow?> GetByModelIdAsync(
        string modelId,
        CancellationToken cancellationToken = default);

    Task<AgentModelRow> CreateAsync(
        CreateAgentModelInput input,
        CancellationToken cancellationToken = default);

    Task<AgentModelRow?> UpdateAsync(
        Guid id,
        UpdateAgentModelInput input,
        CancellationToken cancellationToken = default);

    // Atomically clears the default flag on every other row before setting
    // it on this one. Returns the updated row, or null if id is missing.
    Task<AgentModelRow?> SetDefaultAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> SetAvailabilityAsync(
        Guid id,
        bool available,
        CancellationToken cancellationToken = default);
}

public sealed record class AgentModelRow(
    Guid Id,
    string ModelId,
    string DisplayName,
    string Provider,
    int ContextWindowTokens,
    decimal? InputCostPerMillionTokens,
    decimal? OutputCostPerMillionTokens,
    string CostCurrency,
    DateTime? CostPublishedAtUtc,
    string? Description,
    bool IsArchived,
    bool IsDefault,
    bool IsAvailable,
    int SortOrder,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record class CreateAgentModelInput(
    string ModelId,
    string DisplayName,
    string Provider,
    int ContextWindowTokens,
    decimal? InputCostPerMillionTokens,
    decimal? OutputCostPerMillionTokens,
    string CostCurrency,
    DateTime? CostPublishedAtUtc,
    string? Description,
    int SortOrder);

// Null fields = leave unchanged. Empty string on Description = clear.
public sealed record class UpdateAgentModelInput(
    string? DisplayName,
    string? Provider,
    int? ContextWindowTokens,
    decimal? InputCostPerMillionTokens,
    decimal? OutputCostPerMillionTokens,
    string? CostCurrency,
    DateTime? CostPublishedAtUtc,
    string? Description,
    int? SortOrder);
