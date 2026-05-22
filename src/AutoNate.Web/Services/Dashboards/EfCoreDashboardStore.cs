using System.Text.Json;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Dashboards;

public sealed class EfCoreDashboardStore(IDbContextFactory<AutoNateDbContext> dbContextFactory) : IDashboardStore
{
    private const string DashboardTemplateKey = "dashboard";

    public async Task<IReadOnlyList<Dashboard>> ListForActorAsync(Guid actorId, CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty) return [];
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        // v1 visibility: owner_user_id matches OR a dashboard_shares row
        // grants the actor access. The share-row branch is dead code in v1
        // (no UI writes shares) but keeps the predicate stable for v2.
        var owned = db.Dashboards.AsNoTracking()
            .Where(d => d.OwnerUserId == actorId);
        var shared = db.Dashboards.AsNoTracking()
            .Where(d => db.DashboardShares.Any(s =>
                s.DashboardId == d.Id
                && s.PrincipalType == "user"
                && s.PrincipalId == actorId));
        return await owned
            .Union(shared)
            .OrderByDescending(d => d.UpdatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<DashboardWithWidgets?> GetForActorAsync(Guid id, Guid actorId, CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty) return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var dashboard = await db.Dashboards.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (dashboard is null) return null;
        if (!await ActorIsAuthorizedAsync(db, dashboard, actorId, cancellationToken)) return null;

        var widgets = await db.DashboardWidgets.AsNoTracking()
            .Where(w => w.DashboardId == id)
            .OrderBy(w => w.SortOrder).ThenBy(w => w.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return new DashboardWithWidgets(dashboard, widgets);
    }

    public async Task<Dashboard> CreateAsync(CreateDashboardInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new ArgumentException("Dashboard name is required.", nameof(input));
        if (actorId == Guid.Empty)
            throw new InvalidOperationException("Cannot create a dashboard for an empty actor id.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var dashboard = new Dashboard
        {
            Id = Guid.NewGuid(),
            OwnerUserId = actorId,
            Name = input.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            Visibility = "private",
            Scope = "user",
            Source = "user",
            TemplateKey = DashboardTemplateKey,
            SettingsJsonb = "{}",
            IsArchived = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedBy = actorId
        };
        db.Dashboards.Add(dashboard);

        // If the dashboard was created from a mount-point with a locked
        // default layout, seed the new dashboard with widgets copied from
        // menu_items.config.defaultLayout. Failure to parse is silent —
        // the user just gets an empty dashboard rather than a 500.
        if (!string.IsNullOrWhiteSpace(input.FromMountPath))
        {
            var defaultLayout = await ReadDefaultLayoutAsync(db, input.FromMountPath!, cancellationToken);
            if (defaultLayout is { } seeds)
            {
                var sortOrder = 0;
                foreach (var seed in seeds)
                {
                    db.DashboardWidgets.Add(new DashboardWidget
                    {
                        Id = Guid.NewGuid(),
                        DashboardId = dashboard.Id,
                        WidgetType = seed.WidgetType,
                        Title = seed.Title,
                        ConfigJsonb = seed.Config,
                        GridX = seed.GridX,
                        GridY = seed.GridY,
                        GridW = seed.GridW,
                        GridH = seed.GridH,
                        SortOrder = sortOrder++,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    });
                }
                if (seeds.Count > 0)
                {
                    dashboard.Source = "template";
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return dashboard;
    }

    public async Task<Dashboard> UpdateAsync(Guid id, UpdateDashboardInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var dashboard = await db.Dashboards.FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new DashboardNotFoundException(id);
        // Editing requires ownership in v1 — share editor role would
        // unlock it later, but `dashboard_shares` has no rows yet.
        if (dashboard.OwnerUserId != actorId) throw new DashboardNotFoundException(id);

        var changed = false;
        if (input.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(input.Name))
                throw new ArgumentException("Dashboard name cannot be empty.", nameof(input));
            var trimmed = input.Name.Trim();
            if (dashboard.Name != trimmed)
            {
                dashboard.Name = trimmed;
                changed = true;
            }
        }
        if (input.Description is not null)
        {
            var trimmed = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
            if (dashboard.Description != trimmed)
            {
                dashboard.Description = trimmed;
                changed = true;
            }
        }
        if (input.Settings is { } settings && settings.ValueKind != JsonValueKind.Undefined)
        {
            var json = settings.GetRawText();
            if (dashboard.SettingsJsonb != json)
            {
                dashboard.SettingsJsonb = json;
                changed = true;
            }
        }

        if (changed)
        {
            dashboard.UpdatedAtUtc = DateTime.UtcNow;
            dashboard.UpdatedBy = actorId;
            await db.SaveChangesAsync(cancellationToken);
        }
        return dashboard;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var dashboard = await db.Dashboards.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (dashboard is null) return false;
        if (dashboard.OwnerUserId != actorId) return false;
        db.Dashboards.Remove(dashboard);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<DashboardWidget> AddWidgetAsync(Guid dashboardId, CreateWidgetInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.WidgetType))
            throw new ArgumentException("Widget type is required.", nameof(input));

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var dashboard = await db.Dashboards.FirstOrDefaultAsync(d => d.Id == dashboardId, cancellationToken)
            ?? throw new DashboardNotFoundException(dashboardId);
        if (dashboard.OwnerUserId != actorId) throw new DashboardNotFoundException(dashboardId);

        var nextSortOrder = await db.DashboardWidgets
            .Where(w => w.DashboardId == dashboardId)
            .Select(w => (int?)w.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;

        var now = DateTime.UtcNow;
        var widget = new DashboardWidget
        {
            Id = Guid.NewGuid(),
            DashboardId = dashboardId,
            WidgetType = input.WidgetType.Trim(),
            Title = string.IsNullOrWhiteSpace(input.Title) ? null : input.Title.Trim(),
            ConfigJsonb = input.Config.ValueKind == JsonValueKind.Undefined ? "{}" : input.Config.GetRawText(),
            GridX = input.GridX,
            GridY = input.GridY,
            GridW = input.GridW,
            GridH = input.GridH,
            SortOrder = nextSortOrder + 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.DashboardWidgets.Add(widget);

        dashboard.UpdatedAtUtc = now;
        dashboard.UpdatedBy = actorId;

        await db.SaveChangesAsync(cancellationToken);
        return widget;
    }

    public async Task<DashboardWidget> UpdateWidgetAsync(Guid dashboardId, Guid widgetId, UpdateWidgetInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var dashboard = await db.Dashboards.FirstOrDefaultAsync(d => d.Id == dashboardId, cancellationToken)
            ?? throw new DashboardNotFoundException(dashboardId);
        if (dashboard.OwnerUserId != actorId) throw new DashboardNotFoundException(dashboardId);

        var widget = await db.DashboardWidgets.FirstOrDefaultAsync(
            w => w.Id == widgetId && w.DashboardId == dashboardId, cancellationToken)
            ?? throw new DashboardWidgetNotFoundException(widgetId);

        var changed = false;
        if (input.Title is not null)
        {
            var trimmed = string.IsNullOrWhiteSpace(input.Title) ? null : input.Title.Trim();
            if (widget.Title != trimmed)
            {
                widget.Title = trimmed;
                changed = true;
            }
        }
        if (input.Config is { } config && config.ValueKind != JsonValueKind.Undefined)
        {
            var json = config.GetRawText();
            if (widget.ConfigJsonb != json)
            {
                widget.ConfigJsonb = json;
                changed = true;
            }
        }
        if (input.GridX is { } gx && widget.GridX != gx) { widget.GridX = gx; changed = true; }
        if (input.GridY is { } gy && widget.GridY != gy) { widget.GridY = gy; changed = true; }
        if (input.GridW is { } gw && widget.GridW != gw) { widget.GridW = gw; changed = true; }
        if (input.GridH is { } gh && widget.GridH != gh) { widget.GridH = gh; changed = true; }

        if (changed)
        {
            var now = DateTime.UtcNow;
            widget.UpdatedAtUtc = now;
            dashboard.UpdatedAtUtc = now;
            dashboard.UpdatedBy = actorId;
            await db.SaveChangesAsync(cancellationToken);
        }
        return widget;
    }

    public async Task<bool> RemoveWidgetAsync(Guid dashboardId, Guid widgetId, Guid actorId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var dashboard = await db.Dashboards.FirstOrDefaultAsync(d => d.Id == dashboardId, cancellationToken);
        if (dashboard is null) return false;
        if (dashboard.OwnerUserId != actorId) return false;
        var widget = await db.DashboardWidgets.FirstOrDefaultAsync(
            w => w.Id == widgetId && w.DashboardId == dashboardId, cancellationToken);
        if (widget is null) return false;
        db.DashboardWidgets.Remove(widget);

        var now = DateTime.UtcNow;
        dashboard.UpdatedAtUtc = now;
        dashboard.UpdatedBy = actorId;

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> ReplaceLayoutAsync(Guid dashboardId, IReadOnlyList<LayoutPosition> positions, Guid actorId, CancellationToken cancellationToken = default)
    {
        if (positions.Count == 0) return 0;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var dashboard = await db.Dashboards.FirstOrDefaultAsync(d => d.Id == dashboardId, cancellationToken)
            ?? throw new DashboardNotFoundException(dashboardId);
        if (dashboard.OwnerUserId != actorId) throw new DashboardNotFoundException(dashboardId);

        var widgetIds = positions.Select(p => p.WidgetId).ToList();
        var widgets = await db.DashboardWidgets
            .Where(w => w.DashboardId == dashboardId && widgetIds.Contains(w.Id))
            .ToListAsync(cancellationToken);
        var byId = widgets.ToDictionary(w => w.Id);

        var now = DateTime.UtcNow;
        var updated = 0;
        foreach (var p in positions)
        {
            if (!byId.TryGetValue(p.WidgetId, out var w)) continue;
            var changed = w.GridX != p.GridX || w.GridY != p.GridY
                || w.GridW != p.GridW || w.GridH != p.GridH;
            if (!changed) continue;
            w.GridX = p.GridX;
            w.GridY = p.GridY;
            w.GridW = p.GridW;
            w.GridH = p.GridH;
            w.UpdatedAtUtc = now;
            updated++;
        }
        if (updated > 0)
        {
            dashboard.UpdatedAtUtc = now;
            dashboard.UpdatedBy = actorId;
            await db.SaveChangesAsync(cancellationToken);
        }
        return updated;
    }

    private static async Task<bool> ActorIsAuthorizedAsync(
        AutoNateDbContext db,
        Dashboard dashboard,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        if (dashboard.OwnerUserId == actorId) return true;
        return await db.DashboardShares.AsNoTracking()
            .AnyAsync(s => s.DashboardId == dashboard.Id
                && s.PrincipalType == "user"
                && s.PrincipalId == actorId, cancellationToken);
    }

    private sealed record DefaultWidgetSeed(
        string WidgetType,
        string? Title,
        string Config,
        int GridX,
        int GridY,
        int GridW,
        int GridH);

    // Reads menu_items.config.defaultLayout.widgets for the mount-point at
    // {path}. Returns null when the mount has no template default. Shape is
    // owned by the SPA; we just copy the per-widget primitives.
    private static async Task<IReadOnlyList<DefaultWidgetSeed>?> ReadDefaultLayoutAsync(
        AutoNateDbContext db,
        string path,
        CancellationToken cancellationToken)
    {
        var configJson = await db.MenuItems
            .FromSqlInterpolated($@"
                SELECT * FROM menu_items
                WHERE item_type = 'template'
                  AND config->>'templateKey' = 'dashboard'
                  AND config->>'path' = {path}
                LIMIT 1")
            .AsNoTracking()
            .Select(m => m.Config)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(configJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("defaultLayout", out var layout)) return null;
            if (!layout.TryGetProperty("widgets", out var widgets)
                || widgets.ValueKind != JsonValueKind.Array) return null;

            var seeds = new List<DefaultWidgetSeed>(widgets.GetArrayLength());
            foreach (var w in widgets.EnumerateArray())
            {
                var widgetType = w.TryGetProperty("widgetType", out var wt) && wt.ValueKind == JsonValueKind.String
                    ? wt.GetString() : null;
                if (string.IsNullOrWhiteSpace(widgetType)) continue;
                seeds.Add(new DefaultWidgetSeed(
                    WidgetType: widgetType!,
                    Title: w.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
                    Config: w.TryGetProperty("config", out var c) && c.ValueKind != JsonValueKind.Undefined ? c.GetRawText() : "{}",
                    GridX: w.TryGetProperty("gridX", out var gx) && gx.TryGetInt32(out var ix) ? ix : 0,
                    GridY: w.TryGetProperty("gridY", out var gy) && gy.TryGetInt32(out var iy) ? iy : 0,
                    GridW: w.TryGetProperty("gridW", out var gw) && gw.TryGetInt32(out var iw) ? iw : 4,
                    GridH: w.TryGetProperty("gridH", out var gh) && gh.TryGetInt32(out var ih) ? ih : 3));
            }
            return seeds;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
