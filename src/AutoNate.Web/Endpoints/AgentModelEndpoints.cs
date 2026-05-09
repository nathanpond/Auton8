using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Agent.Catalog;
using AutoNate.Web.Services.ExternalConnections;

namespace AutoNate.Web.Endpoints;

// Catalogue admin: list / create / edit / archive of agent_model rows.
// Permission-gated through EntityKinds.ExternalConnection because the same
// admin who configures LLM api keys curates the model catalogue. View
// includes the catalogue listing (used to populate the External Connections
// model dropdown); Manage gates writes.
public static class AgentModelEndpoints
{
    // Maps the External Connection kind discriminator to the catalog
    // provider name. When asking "does this model's provider have a
    // configured connection", we go provider -> connection-kinds -> any
    // enabled row.
    private static readonly IReadOnlyDictionary<string, string> ProviderToConnectionKind = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Anthropic"] = "LlmProvider:Anthropic",
        ["OpenAI"] = "LlmProvider:OpenAI"
    };

    public static IEndpointRouteBuilder MapAgentModelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/agent-models").RequireAuthorization();

        group.MapGet("/", async (
            string? provider,
            IAgentModelCatalogStore store,
            IExternalConnectionStore connectionStore,
            CancellationToken ct) =>
        {
            var rows = await store.ListAsync(provider, ct);
            var providersWithConnection = await ProvidersWithEnabledConnectionAsync(connectionStore, ct);
            return Results.Ok(rows.Select(r => ToView(r, providersWithConnection)).ToList());
        }).RequireKindPermission(EntityKinds.ExternalConnection, Actions.View);

        group.MapGet("/{id:guid}", async (
            Guid id,
            IAgentModelCatalogStore store,
            IExternalConnectionStore connectionStore,
            CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            if (row is null) return Results.NotFound();
            var providersWithConnection = await ProvidersWithEnabledConnectionAsync(connectionStore, ct);
            return Results.Ok(ToView(row, providersWithConnection));
        }).RequireKindPermission(EntityKinds.ExternalConnection, Actions.View);

        // No POST endpoint — the catalogue is provider-curated. Rows
        // arrive via /refresh (which calls IAgentModelCatalogStore
        // internally) and admins edit cost / description / display name
        // via PUT but never invent new model_ids.

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateAgentModelRequest request,
            IAgentModelCatalogStore store,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var row = await store.UpdateAsync(id, new UpdateAgentModelInput(
                DisplayName: request.DisplayName,
                Provider: request.Provider,
                ContextWindowTokens: request.ContextWindowTokens,
                InputCostPerMillionTokens: request.InputCostPerMillionTokens,
                OutputCostPerMillionTokens: request.OutputCostPerMillionTokens,
                CostCurrency: request.CostCurrency,
                CostPublishedAtUtc: request.CostPublishedAtUtc,
                Description: request.Description,
                SortOrder: request.SortOrder), ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        }).RequireKindPermission(EntityKinds.ExternalConnection, Actions.Manage)
          .DisableAntiforgery();

        // Archive / unarchive removed — the catalogue is provider-curated
        // and admins shouldn't take rows out of the listing manually.
        // is_archived stays in the schema (defensive) but isn't writable
        // through the API.

        group.MapPost("/{id:guid}/set-default", async (
            Guid id,
            IAgentModelCatalogStore store,
            IExternalConnectionStore connectionStore,
            CancellationToken ct) =>
        {
            var existing = await store.GetAsync(id, ct);
            if (existing is null) return Results.NotFound();
            if (!await ProviderHasEnabledConnectionAsync(connectionStore, existing.Provider, ct))
            {
                return Results.BadRequest(new
                {
                    reason = $"Cannot set default: no enabled External Connection is configured for provider '{existing.Provider}'."
                });
            }
            var row = await store.SetDefaultAsync(id, ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        }).RequireKindPermission(EntityKinds.ExternalConnection, Actions.Manage)
          .DisableAntiforgery();

        group.MapPost("/{id:guid}/set-available", async (
            Guid id,
            IAgentModelCatalogStore store,
            IExternalConnectionStore connectionStore,
            CancellationToken ct) =>
        {
            var existing = await store.GetAsync(id, ct);
            if (existing is null) return Results.NotFound();
            if (!await ProviderHasEnabledConnectionAsync(connectionStore, existing.Provider, ct))
            {
                return Results.BadRequest(new
                {
                    reason = $"Cannot mark available: no enabled External Connection is configured for provider '{existing.Provider}'."
                });
            }
            var ok = await store.SetAvailabilityAsync(id, available: true, ct);
            return ok ? Results.NoContent() : Results.NotFound();
        }).RequireKindPermission(EntityKinds.ExternalConnection, Actions.Manage)
          .DisableAntiforgery();

        group.MapPost("/{id:guid}/set-unavailable", async (
            Guid id,
            IAgentModelCatalogStore store,
            CancellationToken ct) =>
        {
            var ok = await store.SetAvailabilityAsync(id, available: false, ct);
            return ok ? Results.NoContent() : Results.NotFound();
        }).RequireKindPermission(EntityKinds.ExternalConnection, Actions.Manage)
          .DisableAntiforgery();

        group.MapPost("/refresh", async (
            IAgentModelCatalogRefresher refresher,
            CancellationToken ct) =>
        {
            var result = await refresher.RefreshAsync(ct);
            return Results.Ok(result);
        }).RequireKindPermission(EntityKinds.ExternalConnection, Actions.Manage)
          .DisableAntiforgery();

        return app;
    }

    // True iff the catalog provider name has at least one enabled
    // External Connection of the matching kind. Single-row check, used
    // by /set-default and /set-available.
    private static async Task<bool> ProviderHasEnabledConnectionAsync(
        IExternalConnectionStore connectionStore,
        string provider,
        CancellationToken cancellationToken)
    {
        if (!ProviderToConnectionKind.TryGetValue(provider, out var kind)) return false;
        var rows = await connectionStore.ListAsync(kind, cancellationToken);
        return rows.Any(r => r.IsEnabled);
    }

    // The same data, but answering "for which providers does at least one
    // enabled connection exist" in a single round trip — for the LIST
    // endpoint we need this for every row, not just one.
    private static async Task<HashSet<string>> ProvidersWithEnabledConnectionAsync(
        IExternalConnectionStore connectionStore,
        CancellationToken cancellationToken)
    {
        var rows = await connectionStore.ListAsync(kind: null, cancellationToken);
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows.Where(r => r.IsEnabled))
        {
            // Reverse lookup connection-kind -> provider name.
            foreach (var (provider, connectionKind) in ProviderToConnectionKind)
            {
                if (string.Equals(connectionKind, r.Kind, StringComparison.OrdinalIgnoreCase))
                {
                    present.Add(provider);
                }
            }
        }
        return present;
    }

    private static AgentModelView ToView(AgentModelRow row, HashSet<string> providersWithConnection) => new(
        Id: row.Id,
        ModelId: row.ModelId,
        DisplayName: row.DisplayName,
        Provider: row.Provider,
        ContextWindowTokens: row.ContextWindowTokens,
        InputCostPerMillionTokens: row.InputCostPerMillionTokens,
        OutputCostPerMillionTokens: row.OutputCostPerMillionTokens,
        CostCurrency: row.CostCurrency,
        CostPublishedAtUtc: row.CostPublishedAtUtc,
        Description: row.Description,
        IsArchived: row.IsArchived,
        IsDefault: row.IsDefault,
        IsAvailable: row.IsAvailable,
        ProviderHasConnection: providersWithConnection.Contains(row.Provider),
        SortOrder: row.SortOrder,
        CreatedAtUtc: row.CreatedAtUtc,
        UpdatedAtUtc: row.UpdatedAtUtc);
}

// API view of an agent_model row. Wraps the storage-shape AgentModelRow
// with the computed ProviderHasConnection flag so the SPA can disable
// "Set as default" / "Make available" actions for providers that lack a
// configured External Connection.
public sealed record class AgentModelView(
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
    bool ProviderHasConnection,
    int SortOrder,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record class UpdateAgentModelRequest(
    string? DisplayName,
    string? Provider,
    int? ContextWindowTokens,
    decimal? InputCostPerMillionTokens,
    decimal? OutputCostPerMillionTokens,
    string? CostCurrency,
    DateTime? CostPublishedAtUtc,
    string? Description,
    int? SortOrder);
