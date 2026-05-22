using System.Text.Json;
using AutoNate.Web.Persistence.Scaffolded;

namespace AutoNate.Web.Services.Dashboards;

public sealed record class CreateDashboardInput(
    string Name,
    string? Description,
    string? FromMountPath);

public sealed record class UpdateDashboardInput(
    string? Name,
    string? Description,
    JsonElement? Settings);

public sealed record class CreateWidgetInput(
    string WidgetType,
    string? Title,
    JsonElement Config,
    int GridX,
    int GridY,
    int GridW,
    int GridH);

public sealed record class UpdateWidgetInput(
    string? Title,
    JsonElement? Config,
    int? GridX,
    int? GridY,
    int? GridW,
    int? GridH);

// Used by the bulk-position endpoint after drag/resize ends. The client
// always sends every widget in the dashboard, so the store does one tx
// instead of N PATCHes.
public sealed record class LayoutPosition(
    Guid WidgetId,
    int GridX,
    int GridY,
    int GridW,
    int GridH);

public sealed record class DashboardWithWidgets(
    Dashboard Dashboard,
    IReadOnlyList<DashboardWidget> Widgets);

public sealed class DashboardNotFoundException(Guid id)
    : Exception($"Dashboard '{id}' was not found.");

public sealed class DashboardForbiddenException(Guid id)
    : Exception($"Actor is not authorized for dashboard '{id}'.");

public sealed class DashboardWidgetNotFoundException(Guid id)
    : Exception($"Dashboard widget '{id}' was not found.");

public interface IDashboardStore
{
    // Lists every dashboard the actor can see. v1: owned + share rows
    // (dashboard_shares is empty in v1 so this returns owned-only). The
    // store does the visibility filter inline so callers never need to.
    Task<IReadOnlyList<Dashboard>> ListForActorAsync(Guid actorId, CancellationToken cancellationToken = default);

    // Returns null when the row doesn't exist OR when the actor is not
    // entitled to see it. The endpoint maps null to 404 — owner-only
    // entities stay invisible (not 403, which would acknowledge their
    // existence).
    Task<DashboardWithWidgets?> GetForActorAsync(Guid id, Guid actorId, CancellationToken cancellationToken = default);

    Task<Dashboard> CreateAsync(CreateDashboardInput input, Guid actorId, CancellationToken cancellationToken = default);

    // Throws DashboardNotFoundException when the row is missing AND when the
    // actor is not the owner — endpoint code maps both to 404.
    Task<Dashboard> UpdateAsync(Guid id, UpdateDashboardInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, Guid actorId, CancellationToken cancellationToken = default);

    Task<DashboardWidget> AddWidgetAsync(Guid dashboardId, CreateWidgetInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<DashboardWidget> UpdateWidgetAsync(Guid dashboardId, Guid widgetId, UpdateWidgetInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<bool> RemoveWidgetAsync(Guid dashboardId, Guid widgetId, Guid actorId, CancellationToken cancellationToken = default);

    Task<int> ReplaceLayoutAsync(Guid dashboardId, IReadOnlyList<LayoutPosition> positions, Guid actorId, CancellationToken cancellationToken = default);
}
