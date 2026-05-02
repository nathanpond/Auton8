using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Models.Menus;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using MenuEntity = AutoNate.Web.Persistence.Scaffolded.Menu;
using MenuItemEntity = AutoNate.Web.Persistence.Scaffolded.MenuItem;

namespace AutoNate.Web.Services.Menus;

public sealed class EfCoreMenuStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IAuthorizer authorizer) : IMenuStore
{
    private static readonly HashSet<string> AllowedItemTypes = new(StringComparer.Ordinal)
    {
        "group", "link", "route", "page", "action", "separator", "template"
    };

    private static readonly HashSet<string> ItemTypesWithoutDisplayName = new(StringComparer.Ordinal)
    {
        "separator"
    };

    public async Task<IReadOnlyList<Menu>> ListMenusAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Menus.AsNoTracking()
            .OrderBy(m => m.Name)
            .ToListAsync(cancellationToken);
        return rows.Select(ToMenuModel).ToList();
    }

    public async Task<Menu?> GetMenuTreeAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var menu = await db.Menus.AsNoTracking()
            .SingleOrDefaultAsync(m => m.Key == key, cancellationToken);
        if (menu is null) return null;

        var items = await db.MenuItems.AsNoTracking()
            .Where(i => i.MenuId == menu.Id)
            .OrderBy(i => i.SortOrder)
            .ToListAsync(cancellationToken);

        return ToMenuModel(menu) with { Items = BuildTree(items, parentId: null) };
    }

    public async Task<Menu?> GetMenuTreeForActorAsync(string key, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        var tree = await GetMenuTreeAsync(key, cancellationToken);
        if (tree is null) return null;

        var permissionCache = new Dictionary<string, bool>(StringComparer.Ordinal);

        async Task<IReadOnlyList<MenuItem>> FilterAsync(IReadOnlyList<MenuItem> nodes)
        {
            var kept = new List<MenuItem>(nodes.Count);
            foreach (var node in nodes)
            {
                if (!node.IsVisible) continue;
                if (!await IsAllowedAsync(node.PermissionRequired, permissionCache, actor, cancellationToken))
                {
                    continue;
                }

                var children = node.Children.Count == 0
                    ? Array.Empty<MenuItem>()
                    : await FilterAsync(node.Children);
                kept.Add(node with { Children = children });
            }
            return kept;
        }

        var filtered = await FilterAsync(tree.Items);
        return tree with { Items = filtered };
    }

    public async Task<Menu> CreateMenuAsync(CreateMenuInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var key = (input.Key ?? string.Empty).Trim();
        var name = (input.Name ?? string.Empty).Trim();
        if (key.Length == 0) throw new MenuValidationException("key is required.");
        if (name.Length == 0) throw new MenuValidationException("name is required.");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Menus.AnyAsync(m => m.Key == key, cancellationToken))
        {
            throw new MenuValidationException($"Menu with key '{key}' already exists.");
        }

        var now = DateTime.UtcNow;
        var entity = new MenuEntity
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = name,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            IsSystem = false,
            CreatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedAtUtc = now,
            UpdatedBy = actorId
        };
        db.Menus.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return ToMenuModel(entity);
    }

    public async Task<Menu> UpdateMenuAsync(Guid id, UpdateMenuInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Menus.SingleOrDefaultAsync(m => m.Id == id, cancellationToken)
            ?? throw new MenuNotFoundException(id.ToString());

        var changed = false;
        if (input.Name is { } newNameRaw)
        {
            var newName = newNameRaw.Trim();
            if (newName.Length == 0) throw new MenuValidationException("name cannot be empty.");
            if (!string.Equals(entity.Name, newName, StringComparison.Ordinal))
            {
                entity.Name = newName;
                changed = true;
            }
        }
        if (input.Description is { } newDescRaw)
        {
            var newDesc = string.IsNullOrWhiteSpace(newDescRaw) ? null : newDescRaw.Trim();
            if (!string.Equals(entity.Description, newDesc, StringComparison.Ordinal))
            {
                entity.Description = newDesc;
                changed = true;
            }
        }

        if (changed)
        {
            entity.UpdatedAtUtc = DateTime.UtcNow;
            entity.UpdatedBy = actorId;
            await db.SaveChangesAsync(cancellationToken);
        }

        return ToMenuModel(entity);
    }

    public async Task<bool> DeleteMenuAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Menus.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (entity is null) return false;
        if (entity.IsSystem) throw new MenuValidationException("System menus cannot be deleted.");
        db.Menus.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Menu?> GetMenuByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Menus.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        return entity is null ? null : ToMenuModel(entity);
    }

    public async Task<MenuItem?> GetItemByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.MenuItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        return entity is null ? null : ToItemModel(entity);
    }

    public async Task<MenuItem> CreateItemAsync(string menuKey, CreateMenuItemInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateItemType(input.ItemType);
        var displayName = (input.DisplayName ?? string.Empty).Trim();
        if (displayName.Length == 0 && !ItemTypesWithoutDisplayName.Contains(input.ItemType))
        {
            throw new MenuValidationException("displayName is required.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var menu = await db.Menus.SingleOrDefaultAsync(m => m.Key == menuKey, cancellationToken)
            ?? throw new MenuNotFoundException(menuKey);

        if (input.ParentId is { } pid)
        {
            var parentBelongs = await db.MenuItems
                .AnyAsync(i => i.Id == pid && i.MenuId == menu.Id, cancellationToken);
            if (!parentBelongs)
            {
                throw new MenuValidationException("parentId must reference an item in the same menu.");
            }
        }

        var now = DateTime.UtcNow;
        var entity = new MenuItemEntity
        {
            Id = Guid.NewGuid(),
            MenuId = menu.Id,
            ParentId = input.ParentId,
            SortOrder = input.SortOrder,
            DisplayName = displayName,
            Icon = string.IsNullOrWhiteSpace(input.Icon) ? null : input.Icon.Trim(),
            ItemType = input.ItemType,
            Config = SerializeConfig(input.Config),
            PermissionRequired = string.IsNullOrWhiteSpace(input.PermissionRequired) ? null : input.PermissionRequired.Trim(),
            IsVisible = input.IsVisible,
            IsSystem = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.MenuItems.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return ToItemModel(entity);
    }

    public async Task<MenuItem> UpdateItemAsync(Guid id, UpdateMenuItemInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.MenuItems.SingleOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw new MenuItemNotFoundException(id);

        var changed = false;

        if (input.DisplayName is { } newName)
        {
            var trimmed = newName.Trim();
            if (trimmed.Length == 0 && !ItemTypesWithoutDisplayName.Contains(entity.ItemType))
            {
                throw new MenuValidationException("displayName cannot be empty.");
            }
            if (!string.Equals(entity.DisplayName, trimmed, StringComparison.Ordinal))
            {
                entity.DisplayName = trimmed;
                changed = true;
            }
        }

        if (input.ItemType is { } newType)
        {
            ValidateItemType(newType);
            if (!string.Equals(entity.ItemType, newType, StringComparison.Ordinal))
            {
                entity.ItemType = newType;
                changed = true;
            }
        }

        if (input.ParentId is { } newParent)
        {
            if (newParent != Guid.Empty)
            {
                var parentBelongs = await db.MenuItems.AnyAsync(
                    i => i.Id == newParent && i.MenuId == entity.MenuId,
                    cancellationToken);
                if (!parentBelongs)
                {
                    throw new MenuValidationException("parentId must reference an item in the same menu.");
                }
                if (newParent == entity.Id)
                {
                    throw new MenuValidationException("An item cannot be its own parent.");
                }
            }
            var newParentValue = newParent == Guid.Empty ? (Guid?)null : newParent;
            if (entity.ParentId != newParentValue)
            {
                entity.ParentId = newParentValue;
                changed = true;
            }
        }

        if (input.SortOrder is { } newSort && entity.SortOrder != newSort)
        {
            entity.SortOrder = newSort;
            changed = true;
        }

        if (input.ClearIcon)
        {
            if (entity.Icon is not null) { entity.Icon = null; changed = true; }
        }
        else if (input.Icon is { } newIcon)
        {
            var trimmed = string.IsNullOrWhiteSpace(newIcon) ? null : newIcon.Trim();
            if (!string.Equals(entity.Icon, trimmed, StringComparison.Ordinal))
            {
                entity.Icon = trimmed;
                changed = true;
            }
        }

        if (input.Config is { } newConfig)
        {
            var serialized = SerializeConfig(newConfig);
            if (!string.Equals(entity.Config, serialized, StringComparison.Ordinal))
            {
                entity.Config = serialized;
                changed = true;
            }
        }

        if (input.ClearPermissionRequired)
        {
            if (entity.PermissionRequired is not null) { entity.PermissionRequired = null; changed = true; }
        }
        else if (input.PermissionRequired is { } newPerm)
        {
            var trimmed = string.IsNullOrWhiteSpace(newPerm) ? null : newPerm.Trim();
            if (!string.Equals(entity.PermissionRequired, trimmed, StringComparison.Ordinal))
            {
                entity.PermissionRequired = trimmed;
                changed = true;
            }
        }

        if (input.IsVisible is { } newVisible && entity.IsVisible != newVisible)
        {
            entity.IsVisible = newVisible;
            changed = true;
        }

        if (changed)
        {
            entity.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return ToItemModel(entity);
    }

    public async Task<bool> DeleteItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.MenuItems.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (entity is null) return false;
        db.MenuItems.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ReplaceTreeAsync(string menuKey, IReadOnlyList<TreeNodeInput> nodes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var menu = await db.Menus.SingleOrDefaultAsync(m => m.Key == menuKey, cancellationToken)
            ?? throw new MenuNotFoundException(menuKey);

        var items = await db.MenuItems
            .Where(i => i.MenuId == menu.Id)
            .ToListAsync(cancellationToken);
        var byId = items.ToDictionary(i => i.Id);

        // Validate all referenced ids belong to this menu.
        var seen = new HashSet<Guid>();
        foreach (var node in nodes)
        {
            if (!byId.ContainsKey(node.Id))
            {
                throw new MenuValidationException($"Item '{node.Id}' does not belong to menu '{menuKey}'.");
            }
            if (!seen.Add(node.Id))
            {
                throw new MenuValidationException($"Item '{node.Id}' appears more than once in the tree.");
            }
            if (node.ParentId is { } pid && pid != Guid.Empty && !byId.ContainsKey(pid))
            {
                throw new MenuValidationException($"Parent '{pid}' does not belong to menu '{menuKey}'.");
            }
        }

        // Detect cycles by walking parent chains.
        foreach (var node in nodes)
        {
            var current = node.ParentId;
            var hops = 0;
            while (current is { } cur && cur != Guid.Empty)
            {
                if (cur == node.Id)
                {
                    throw new MenuValidationException("Tree contains a cycle.");
                }
                var next = nodes.FirstOrDefault(n => n.Id == cur);
                current = next?.ParentId;
                if (++hops > 1000)
                {
                    throw new MenuValidationException("Tree depth exceeds the maximum.");
                }
            }
        }

        var now = DateTime.UtcNow;
        foreach (var node in nodes)
        {
            var entity = byId[node.Id];
            var newParent = node.ParentId == Guid.Empty ? (Guid?)null : node.ParentId;
            if (entity.ParentId != newParent || entity.SortOrder != node.SortOrder)
            {
                entity.ParentId = newParent;
                entity.SortOrder = node.SortOrder;
                entity.UpdatedAtUtc = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PageRegistryEntry>> ListPagesAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        // Pages own their URL via item_type='page'. Routes can also "claim" a
        // URL via config.aliasPath — those become aliases the catch-all
        // resolves to the target route's component. Template items mount a
        // built-in SPA template at their config.path (or the template's own
        // default_path when path is omitted).
        var rows = await db.MenuItems.AsNoTracking()
            .Where(i => (i.ItemType == "page" || i.ItemType == "route" || i.ItemType == "template") && i.IsVisible)
            .ToListAsync(cancellationToken);

        var templateInfo = await LoadTemplateInfoAsync(db, rows, cancellationToken);

        var permissionCache = new Dictionary<string, bool>(StringComparer.Ordinal);
        var entries = new List<PageRegistryEntry>(rows.Count);
        foreach (var row in rows)
        {
            if (!await IsAllowedAsync(row.PermissionRequired, permissionCache, actor, cancellationToken)) continue;
            var (path, contentType) = ParseRegistryEntry(row, templateInfo);
            if (path is null) continue;
            entries.Add(new PageRegistryEntry(row.Id, path, contentType));
        }
        return entries;
    }

    public async Task<PageContent?> GetPageByPathAsync(string path, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.MenuItems.AsNoTracking()
            .Where(i => (i.ItemType == "page" || i.ItemType == "route" || i.ItemType == "template") && i.IsVisible)
            .ToListAsync(cancellationToken);

        var templateInfo = await LoadTemplateInfoAsync(db, rows, cancellationToken);

        var permissionCache = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var (rowPath, contentType, content) = ParsePageOrAlias(row, templateInfo);
            if (rowPath is null) continue;
            if (!string.Equals(rowPath, path, StringComparison.Ordinal)) continue;
            if (!await IsAllowedAsync(row.PermissionRequired, permissionCache, actor, cancellationToken)) return null;
            return new PageContent(row.Id, rowPath, content ?? string.Empty, contentType);
        }
        return null;
    }

    // Snapshot of every page_templates row referenced by a template-typed menu
    // item: default path (used when the menu item omits config.path), the
    // optional plugin-supplied JSX content, and the row's content_type. Lets
    // ParseTemplateConfig serve plugin templates as JSX directly without the
    // SPA needing a per-key React component lookup.
    private readonly record struct TemplateRow(string DefaultPath, string ContentType, string? Content);

    private static async Task<IReadOnlyDictionary<string, TemplateRow>> LoadTemplateInfoAsync(
        AutoNateDbContext db,
        IReadOnlyList<MenuItemEntity> rows,
        CancellationToken cancellationToken)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.ItemType != "template") continue;
            var key = ReadStringField(row.Config, "templateKey");
            if (!string.IsNullOrWhiteSpace(key)) keys.Add(key);
        }
        if (keys.Count == 0)
        {
            return new Dictionary<string, TemplateRow>(StringComparer.Ordinal);
        }
        var templates = await db.PageTemplates.AsNoTracking()
            .Where(t => keys.Contains(t.Key))
            .Select(t => new { t.Key, t.DefaultPath, t.ContentType, t.Content })
            .ToListAsync(cancellationToken);
        return templates.ToDictionary(
            t => t.Key,
            t => new TemplateRow(t.DefaultPath, t.ContentType ?? "builtin", t.Content),
            StringComparer.Ordinal);
    }

    // For an alias-route item, the registry path is the aliasPath and the
    // content type is "alias" (the SPA renders the target component there).
    // For a page item, fall through to the existing page parsing.
    // For a template item, the path is config.path or the template's
    // default_path; content type is "template" for built-in templates and
    // "jsx" for plugin templates (so the SPA's JsxPage renders them).
    private static (string? Path, string ContentType) ParseRegistryEntry(
        MenuItemEntity row,
        IReadOnlyDictionary<string, TemplateRow> templateInfo)
    {
        if (row.ItemType == "route")
        {
            var aliasPath = ReadStringField(row.Config, "aliasPath");
            return aliasPath is null ? (null, "alias") : (aliasPath, "alias");
        }
        if (row.ItemType == "template")
        {
            var (path, contentType, _) = ParseTemplateConfig(row.Config, templateInfo);
            return (path, contentType);
        }
        var (p, ct, _) = ParsePageConfig(row.Config);
        return (p, ct);
    }

    private static (string? Path, string ContentType, string? Content) ParsePageOrAlias(
        MenuItemEntity row,
        IReadOnlyDictionary<string, TemplateRow> templateInfo)
    {
        if (row.ItemType == "route")
        {
            var aliasPath = ReadStringField(row.Config, "aliasPath");
            if (aliasPath is null) return (null, "alias", null);
            // For aliases, content carries the target path so the SPA can
            // render the component for that route at the alias URL.
            var targetPath = ReadStringField(row.Config, "path");
            return (aliasPath, "alias", targetPath);
        }
        if (row.ItemType == "template")
        {
            return ParseTemplateConfig(row.Config, templateInfo);
        }
        return ParsePageConfig(row.Config);
    }

    private static (string? Path, string ContentType, string? Content) ParseTemplateConfig(
        string config,
        IReadOnlyDictionary<string, TemplateRow> templateInfo)
    {
        var key = ReadStringField(config, "templateKey");
        if (string.IsNullOrWhiteSpace(key)) return (null, "template", null);
        var path = ReadStringField(config, "path");
        templateInfo.TryGetValue(key, out var info);
        if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrEmpty(info.DefaultPath))
        {
            path = info.DefaultPath;
        }
        // Plugin-supplied templates carry their own JSX source; serve it as a
        // jsx page so DynamicPageRoute compiles it via JsxPage instead of
        // looking the key up in the SPA's static PAGE_TEMPLATES map.
        if (string.Equals(info.ContentType, "jsx", StringComparison.Ordinal)
            && !string.IsNullOrEmpty(info.Content))
        {
            return (path, "jsx", info.Content);
        }
        return (path, "template", key);
    }

    private static string? ReadStringField(string config, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(config) ? "{}" : config);
            return doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<bool> IsAllowedAsync(
        string? permissionRequired,
        Dictionary<string, bool> cache,
        ClaimsPrincipal actor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(permissionRequired)) return true;
        if (cache.TryGetValue(permissionRequired, out var cached)) return cached;

        var dot = permissionRequired.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot == permissionRequired.Length - 1)
        {
            // Malformed permission strings are treated as deny so admins notice.
            cache[permissionRequired] = false;
            return false;
        }
        var kind = permissionRequired[..dot];
        var action = permissionRequired[(dot + 1)..];

        var decision = await authorizer.AuthorizeAsync(actor, action, new EntityRef(kind, "*"), cancellationToken);
        var allowed = decision.IsAllowed;
        cache[permissionRequired] = allowed;
        return allowed;
    }

    private static void ValidateItemType(string itemType)
    {
        if (string.IsNullOrWhiteSpace(itemType) || !AllowedItemTypes.Contains(itemType))
        {
            throw new MenuValidationException(
                $"itemType '{itemType}' is not one of: {string.Join(", ", AllowedItemTypes)}.");
        }
    }

    private static string SerializeConfig(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined) return "{}";
        return element.GetRawText();
    }

    private static (string? Path, string ContentType, string? Content) ParsePageConfig(string config)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(config) ? "{}" : config);
            var root = doc.RootElement;
            string? path = root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;
            var contentType = root.TryGetProperty("contentType", out var ct) && ct.ValueKind == JsonValueKind.String
                ? ct.GetString() ?? "html"
                : "html";
            var content = root.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;
            return (path, contentType, content);
        }
        catch (JsonException)
        {
            return (null, "html", null);
        }
    }

    private static IReadOnlyList<MenuItem> BuildTree(IReadOnlyList<MenuItemEntity> items, Guid? parentId)
    {
        return items
            .Where(i => i.ParentId == parentId)
            .OrderBy(i => i.SortOrder)
            .Select(i => ToItemModel(i) with { Children = BuildTree(items, i.Id) })
            .ToList();
    }

    private static Menu ToMenuModel(MenuEntity e) => new()
    {
        Id = e.Id,
        Key = e.Key,
        Name = e.Name,
        Description = e.Description,
        IsSystem = e.IsSystem,
        CreatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(e.CreatedAtUtc),
        CreatedBy = e.CreatedBy,
        UpdatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(e.UpdatedAtUtc),
        UpdatedBy = e.UpdatedBy
    };

    private static MenuItem ToItemModel(MenuItemEntity e)
    {
        JsonElement config;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(e.Config) ? "{}" : e.Config);
            config = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var fallback = JsonDocument.Parse("{}");
            config = fallback.RootElement.Clone();
        }

        return new MenuItem
        {
            Id = e.Id,
            MenuId = e.MenuId,
            ParentId = e.ParentId,
            SortOrder = e.SortOrder,
            DisplayName = e.DisplayName,
            Icon = e.Icon,
            ItemType = e.ItemType,
            Config = config,
            PermissionRequired = e.PermissionRequired,
            IsVisible = e.IsVisible,
            IsSystem = e.IsSystem,
            CreatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(e.CreatedAtUtc),
            UpdatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(e.UpdatedAtUtc)
        };
    }
}
