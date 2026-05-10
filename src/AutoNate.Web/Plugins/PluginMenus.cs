using System.Text.Json;
using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Globalization;

namespace AutoNate.Web.Plugins;

// Host-side IPluginMenus. Writes menu_items rows tagged with the plugin's id,
// so the lifecycle code (PluginRuntime/PluginManagementService) can sweep them
// on disable, and the FK ON DELETE CASCADE removes any leftovers on delete.
//
// All work runs as the autonate role (plugins can't write to public.menu_items
// from their own role); the helpers open a connection from the host's
// DbContext. Sync API: matches IAutoNatePlugin.Configure's sync signature so
// plugin authors don't need to reach for .GetAwaiter().GetResult().
internal sealed class PluginMenus : IPluginMenus
{
    private static readonly JsonSerializerOptions ConfigJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly Guid _pluginId;
    private readonly ILogger<PluginMenus> _log;

    public PluginMenus(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        Guid pluginId,
        ILogger<PluginMenus> log)
    {
        _dbFactory = dbFactory;
        _pluginId = pluginId;
        _log = log;
    }

    public IReadOnlyList<MenuInfo> ListMenus()
    {
        using var db = _dbFactory.CreateDbContext();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        var menus = new Dictionary<Guid, (string Key, string Name, List<MenuItemInfo> Items)>();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id, key, name FROM menus ORDER BY name;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetGuid(0);
                menus[id] = (reader.GetString(1), reader.GetString(2), new List<MenuItemInfo>());
            }
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText =
                """
                SELECT id, menu_id, parent_id, sort_order, display_name, icon, item_type,
                       config::text, is_visible, is_system, created_by_plugin_id
                FROM menu_items
                ORDER BY menu_id, parent_id NULLS FIRST, sort_order;
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var menuId = reader.GetGuid(1);
                if (!menus.TryGetValue(menuId, out var menu)) continue;

                menu.Items.Add(new MenuItemInfo(
                    Id: reader.GetGuid(0),
                    ParentId: reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    SortOrder: reader.GetInt32(3),
                    DisplayName: reader.GetString(4),
                    Icon: reader.IsDBNull(5) ? null : reader.GetString(5),
                    ItemType: reader.GetString(6),
                    ConfigJson: reader.GetString(7),
                    IsVisible: reader.GetBoolean(8),
                    IsSystem: reader.GetBoolean(9),
                    CreatedByPluginId: reader.IsDBNull(10) ? null : reader.GetGuid(10)));
            }
        }

        return menus
            .Select(kv => new MenuInfo(kv.Key, kv.Value.Key, kv.Value.Name, kv.Value.Items))
            .ToList();
    }

    public Guid AddPluginMenuItem(NewMenuItem item)
    {
        using var db = _dbFactory.CreateDbContext();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        var siteConfigMenuId = LookupMenuId(connection, "site-config")
            ?? throw new InvalidOperationException("site-config menu not found.");
        var pluginsGroupId = LookupTopLevelGroupId(connection, siteConfigMenuId, "Plugins")
            ?? throw new InvalidOperationException(
                "'Plugins' group not found in the site-config menu. Was the host bootstrap run?");

        return InsertMenuItem(connection, siteConfigMenuId, pluginsGroupId, item);
    }

    public Guid AddSiteConfigGroup(string displayName, string? icon, IEnumerable<NewMenuItem> children)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Group display name is required.", nameof(displayName));

        using var db = _dbFactory.CreateDbContext();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        var siteConfigMenuId = LookupMenuId(connection, "site-config")
            ?? throw new InvalidOperationException("site-config menu not found.");

        using var tx = connection.BeginTransaction();
        try
        {
            var groupItem = new NewMenuItem(
                DisplayName: displayName,
                ItemType: "group",
                Icon: icon,
                Config: null,
                SortOrder: null,
                IsVisible: true);

            var groupId = InsertMenuItem(connection, siteConfigMenuId, parentId: null, groupItem, tx);

            var index = 0;
            foreach (var child in children)
            {
                var orderedChild = child.SortOrder is null
                    ? child with { SortOrder = index }
                    : child;
                InsertMenuItem(connection, siteConfigMenuId, groupId, orderedChild, tx);
                index++;
            }

            tx.Commit();
            return groupId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public Guid AddMenuItem(string menuKey, Guid? parentId, NewMenuItem item)
    {
        if (string.IsNullOrWhiteSpace(menuKey))
            throw new ArgumentException("Menu key is required.", nameof(menuKey));

        using var db = _dbFactory.CreateDbContext();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        var menuId = LookupMenuId(connection, menuKey)
            ?? throw new InvalidOperationException($"Menu '{menuKey}' not found.");

        return InsertMenuItem(connection, menuId, parentId, item);
    }

    public int RemoveAll()
    {
        using var db = _dbFactory.CreateDbContext();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }
        var removed = DeleteAllForPlugin(connection, _pluginId);
        if (removed > 0)
        {
            _log.LogInformation(
                "Plugin {PluginId} removed {Count} of its own menu item(s) via Menus.RemoveAll().",
                _pluginId, removed);
        }
        return removed;
    }

    public bool RemoveMenuItem(Guid id)
    {
        using var db = _dbFactory.CreateDbContext();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }
        // Ownership predicate keeps a buggy or malicious plugin from sweeping
        // menu items it never created — only its own rows are touched.
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "DELETE FROM menu_items WHERE id = @id AND created_by_plugin_id = @plugin_id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@plugin_id", _pluginId);
        var rows = cmd.ExecuteNonQuery();
        if (rows > 0)
        {
            _log.LogInformation(
                "Plugin {PluginId} removed menu item {Id}.", _pluginId, id);
        }
        return rows > 0;
    }

    private Guid InsertMenuItem(
        NpgsqlConnection connection,
        Guid menuId,
        Guid? parentId,
        NewMenuItem item,
        NpgsqlTransaction? transaction = null)
    {
        if (string.IsNullOrWhiteSpace(item.DisplayName) && item.ItemType != "separator")
            throw new ArgumentException("Display name is required for non-separator items.");
        if (string.IsNullOrWhiteSpace(item.ItemType))
            throw new ArgumentException("Item type is required.");

        var configJson = item.Config is null ? "{}" : JsonSerializer.Serialize(item.Config, ConfigJsonOptions);
        var sortOrder = item.SortOrder ?? NextSortOrder(connection, menuId, parentId, transaction);
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;

        using var cmd = connection.CreateCommand();
        if (transaction is not null) cmd.Transaction = transaction;
        cmd.CommandText =
            """
            INSERT INTO menu_items (
                id, menu_id, parent_id, sort_order, display_name, icon,
                item_type, config, is_visible, is_system,
                created_at_utc, updated_at_utc, created_by_plugin_id
            )
            VALUES (
                @id, @menu_id, @parent_id, @sort_order, @display_name, @icon,
                @item_type, @config::jsonb, @is_visible, FALSE,
                @now, @now, @plugin_id
            );
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@menu_id", menuId);
        cmd.Parameters.AddWithValue("@parent_id", (object?)parentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sort_order", sortOrder);
        cmd.Parameters.AddWithValue("@display_name", item.DisplayName ?? string.Empty);
        cmd.Parameters.AddWithValue("@icon", (object?)item.Icon ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@item_type", item.ItemType);
        cmd.Parameters.AddWithValue("@config", configJson);
        cmd.Parameters.AddWithValue("@is_visible", item.IsVisible);
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@plugin_id", _pluginId);

        cmd.ExecuteNonQuery();
        _log.LogInformation(
            "Plugin {PluginId} added menu item {Id} ('{DisplayName}') to menu {MenuId}.",
            _pluginId, id, item.DisplayName, menuId);
        return id;
    }

    private static Guid? LookupMenuId(NpgsqlConnection connection, string key)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM menus WHERE key = @key LIMIT 1;";
        cmd.Parameters.AddWithValue("@key", key);
        var result = cmd.ExecuteScalar();
        return result is Guid g ? g : null;
    }

    private static Guid? LookupTopLevelGroupId(NpgsqlConnection connection, Guid menuId, string displayName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT id FROM menu_items
            WHERE menu_id = @menu_id
              AND parent_id IS NULL
              AND item_type = 'group'
              AND display_name = @name
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@menu_id", menuId);
        cmd.Parameters.AddWithValue("@name", displayName);
        var result = cmd.ExecuteScalar();
        return result is Guid g ? g : null;
    }

    private static int NextSortOrder(
        NpgsqlConnection connection,
        Guid menuId,
        Guid? parentId,
        NpgsqlTransaction? transaction)
    {
        using var cmd = connection.CreateCommand();
        if (transaction is not null) cmd.Transaction = transaction;
        cmd.CommandText =
            """
            SELECT COALESCE(MAX(sort_order), -1) + 1
            FROM menu_items
            WHERE menu_id = @menu_id
              AND parent_id IS NOT DISTINCT FROM @parent_id;
            """;
        cmd.Parameters.AddWithValue("@menu_id", menuId);
        cmd.Parameters.AddWithValue("@parent_id", (object?)parentId ?? DBNull.Value);
        var result = cmd.ExecuteScalar();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    // Bulk-removal hook for the lifecycle code. Lives here so the SQL stays in
    // one place and tests can assert against a single seam.
    public static int DeleteAllForPlugin(NpgsqlConnection connection, Guid pluginId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM menu_items WHERE created_by_plugin_id = @plugin_id;";
        cmd.Parameters.AddWithValue("@plugin_id", pluginId);
        return cmd.ExecuteNonQuery();
    }
}
