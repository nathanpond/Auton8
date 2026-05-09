using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Agent.Catalog;

public sealed class EfCoreAgentModelCatalogStore : IAgentModelCatalogStore
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbContextFactory;
    private readonly IAgentModelCatalog _catalog;
    private readonly AgentModelDefaultStreamService _defaultBroadcast;

    public EfCoreAgentModelCatalogStore(
        IDbContextFactory<AutoNateDbContext> dbContextFactory,
        IAgentModelCatalog catalog,
        AgentModelDefaultStreamService defaultBroadcast)
    {
        _dbContextFactory = dbContextFactory;
        _catalog = catalog;
        _defaultBroadcast = defaultBroadcast;
    }

    public async Task<IReadOnlyList<AgentModelRow>> ListAsync(
        string? provider = null,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.AgentModels.AsNoTracking().Where(m => !m.IsArchived);
        if (!string.IsNullOrWhiteSpace(provider))
        {
            query = query.Where(m => m.Provider == provider);
        }
        var rows = await query
            .OrderBy(m => m.Provider)
            .ThenBy(m => m.SortOrder)
            .ThenBy(m => m.ModelId)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRow).ToList();
    }

    public async Task<AgentModelRow?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.AgentModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        return entity is null ? null : ToRow(entity);
    }

    public async Task<AgentModelRow?> GetByModelIdAsync(string modelId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.AgentModels.AsNoTracking().FirstOrDefaultAsync(m => m.ModelId == modelId, cancellationToken);
        return entity is null ? null : ToRow(entity);
    }

    public async Task<AgentModelRow> CreateAsync(CreateAgentModelInput input, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var entity = new AgentModel
        {
            Id = Guid.NewGuid(),
            ModelId = input.ModelId,
            DisplayName = input.DisplayName,
            Provider = input.Provider,
            ContextWindowTokens = input.ContextWindowTokens,
            InputCostPerMillionTokens = input.InputCostPerMillionTokens,
            OutputCostPerMillionTokens = input.OutputCostPerMillionTokens,
            CostCurrency = string.IsNullOrWhiteSpace(input.CostCurrency) ? "USD" : input.CostCurrency,
            CostPublishedAtUtc = input.CostPublishedAtUtc,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description,
            IsArchived = false,
            SortOrder = input.SortOrder,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.AgentModels.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        _catalog.Invalidate();
        return ToRow(entity);
    }

    public async Task<AgentModelRow?> UpdateAsync(Guid id, UpdateAgentModelInput input, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.AgentModels.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (entity is null) return null;

        if (input.DisplayName is not null) entity.DisplayName = input.DisplayName;
        if (input.Provider is not null) entity.Provider = input.Provider;
        if (input.ContextWindowTokens is int ctx) entity.ContextWindowTokens = ctx;
        if (input.InputCostPerMillionTokens.HasValue) entity.InputCostPerMillionTokens = input.InputCostPerMillionTokens;
        if (input.OutputCostPerMillionTokens.HasValue) entity.OutputCostPerMillionTokens = input.OutputCostPerMillionTokens;
        if (input.CostCurrency is not null) entity.CostCurrency = string.IsNullOrWhiteSpace(input.CostCurrency) ? "USD" : input.CostCurrency;
        if (input.CostPublishedAtUtc.HasValue) entity.CostPublishedAtUtc = input.CostPublishedAtUtc;
        if (input.Description is not null) entity.Description = string.IsNullOrEmpty(input.Description) ? null : input.Description;
        if (input.SortOrder is int sort) entity.SortOrder = sort;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        _catalog.Invalidate();
        return ToRow(entity);
    }

    public async Task<AgentModelRow?> SetDefaultAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var target = await dbContext.AgentModels.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (target is null) return null;

        var now = DateTime.UtcNow;
        // Clear every other default in a single round trip so the partial
        // unique index never sees two TRUEs. There's exactly one default
        // catalog-wide (the chatbot's choice).
        await dbContext.AgentModels
            .Where(m => m.Id != id && m.IsDefault)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.IsDefault, false)
                .SetProperty(m => m.UpdatedAtUtc, now), cancellationToken);

        target.IsDefault = true;
        target.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        _catalog.Invalidate();
        // Push the new default to every chatbot SPA over the broadcast
        // websocket so their footer label updates without a refresh.
        await _defaultBroadcast.BroadcastAsync(cancellationToken);
        return ToRow(target);
    }

    public async Task<bool> SetAvailabilityAsync(Guid id, bool available, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.AgentModels.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (entity is null) return false;
        entity.IsAvailable = available;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        _catalog.Invalidate();
        return true;
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
}
