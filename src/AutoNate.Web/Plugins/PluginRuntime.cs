using System.Collections.Concurrent;
using System.Reflection;
using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Hooks;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AutoNate.Web.Plugins;

public sealed record PluginEnableResult(bool Success, string? ErrorMessage);

// Owns the in-memory set of currently-loaded plugins. Enable creates a new
// collectible ALC, instantiates IAutoNatePlugin, calls Configure() with a
// scoped registrar; Disable revokes every hook the plugin registered (the ALC
// stays loaded but inert); Delete same as Disable plus removes files.
//
// All state-changing ops serialize on a SemaphoreSlim so admin actions can't
// half-load a plugin under concurrent enable/disable.
public sealed class PluginRuntime
{
    private readonly HookRegistrar _registrar;
    private readonly IServiceProvider _hostServices;
    private readonly PluginDataAccessRegistry? _dataRegistry;
    private readonly PluginMigrationRunner? _migrationRunner;
    private readonly IDbContextFactory<AutoNateDbContext>? _dbFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PluginRuntime> _log;
    private readonly string _pluginRoot;

    private readonly ConcurrentDictionary<Guid, LoadedPlugin> _loaded = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PluginRuntime(
        HookRegistrar registrar,
        IServiceProvider hostServices,
        IOptions<PluginOptions> options,
        ILoggerFactory loggerFactory,
        PluginDataAccessRegistry? dataRegistry = null,
        PluginMigrationRunner? migrationRunner = null,
        IDbContextFactory<AutoNateDbContext>? dbFactory = null)
    {
        _registrar = registrar;
        _hostServices = hostServices;
        _dataRegistry = dataRegistry;
        _migrationRunner = migrationRunner;
        _dbFactory = dbFactory;
        _loggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger<PluginRuntime>();
        var configured = options.Value.Folder;
        _pluginRoot = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);
    }

    public string PluginRoot => _pluginRoot;

    public bool IsLoaded(Guid id) => _loaded.ContainsKey(id);

    public IReadOnlyCollection<Guid> LoadedIds => (IReadOnlyCollection<Guid>)_loaded.Keys;

    public async Task<PluginEnableResult> EnableAsync(Plugin row, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_loaded.ContainsKey(row.Id))
            {
                return new(true, null); // idempotent
            }

            var folder = Path.Combine(_pluginRoot, row.Id.ToString("D"));
            var entryPath = Path.Combine(folder, row.EntryAssembly);
            if (!File.Exists(entryPath))
            {
                return new(false, $"Entry assembly not found at '{entryPath}'.");
            }

            PluginAssemblyLoadContext alc = new(entryPath);
            ScopedHookRegistrar? scoped = null;
            try
            {
                var assembly = alc.LoadFromAssemblyPath(entryPath);
                Type? pluginType = null;

                if (!string.IsNullOrWhiteSpace(row.EntryType))
                {
                    pluginType = assembly.GetType(row.EntryType, throwOnError: false);
                    if (pluginType is null)
                    {
                        return new(false, $"Type '{row.EntryType}' not found in '{row.EntryAssembly}'.");
                    }
                }
                else
                {
                    pluginType = FindSinglePluginType(assembly);
                    if (pluginType is null)
                    {
                        return new(false,
                            $"No (or multiple) IAutoNatePlugin types found in '{row.EntryAssembly}'; specify entryType in plugin.json.");
                    }
                }

                if (Activator.CreateInstance(pluginType) is not IAutoNatePlugin instance)
                {
                    return new(false, $"Type '{pluginType.FullName}' could not be activated as IAutoNatePlugin.");
                }

                // Plugins uploaded before the data-storage feature won't have
                // a code/password. They can still load and use hooks; data
                // access just throws on use. New uploads always populate both
                // via PluginSchemaProvisioner, so production rows hit the
                // provisioned branch.
                IPluginDataAccess data;
                string contextCode;
                if (!string.IsNullOrEmpty(row.Code)
                    && row.RolePasswordEncrypted is not null
                    && _dataRegistry is not null
                    && _migrationRunner is not null)
                {
                    var migrationOutcome = await _migrationRunner.RunAsync(
                        row.Code, row.RolePasswordEncrypted, folder, ct).ConfigureAwait(false);
                    if (!migrationOutcome.Success)
                    {
                        return new(false,
                            $"Migration '{migrationOutcome.FailedFile}' failed: {migrationOutcome.ErrorMessage}");
                    }

                    data = _dataRegistry.GetOrCreate(row.Code, row.RolePasswordEncrypted);
                    contextCode = row.Code;
                }
                else
                {
                    _log.LogWarning(
                        "Plugin {Id} has no provisioned schema; IPluginContext.Data will throw on use.", row.Id);
                    data = new UnprovisionedPluginDataAccess();
                    contextCode = string.Empty;
                }

                IPluginMenus menus;
                if (_dbFactory is not null)
                {
                    // Wipe any leftover plugin menu items so Configure() runs
                    // against a clean slate. Disable already does this — this
                    // covers the path where the host crashed mid-disable, or
                    // where rows exist from a prior process.
                    await DeletePluginMenuItemsAsync(row.Id, ct).ConfigureAwait(false);
                    menus = new PluginMenus(_dbFactory, row.Id, _loggerFactory.CreateLogger<PluginMenus>());

                    // Sync any .template files this plugin ships under
                    // PageTemplates/ into the host's page_templates table.
                    // Idempotent across enables — UPSERTs by key on rows the
                    // plugin owns, sweeps templates that no longer have a file.
                    await SyncPluginPageTemplatesAsync(row, folder, ct).ConfigureAwait(false);
                }
                else
                {
                    menus = new NoopPluginMenus();
                }

                scoped = new ScopedHookRegistrar(_registrar);
                var context = new PluginContext(row.Id, contextCode, scoped, data, menus, _hostServices);
                instance.Configure(context);

                var loaded = new LoadedPlugin(row.Id, instance.Name, instance.Version, alc, scoped, instance);
                _loaded[row.Id] = loaded;

                _log.LogInformation("Loaded plugin {Id} ({Name} v{Version}).", row.Id, instance.Name, instance.Version);
                return new(true, null);
            }
            catch (Exception ex)
            {
                scoped?.RemoveAllForPlugin();
                _log.LogError(ex, "Failed to enable plugin {Id} ({EntryAssembly}).", row.Id, row.EntryAssembly);
                return new(false, ex.Message);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisableAsync(Guid id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_loaded.TryRemove(id, out var loaded)) return;
            loaded.ScopedRegistrar.RemoveAllForPlugin();
            _log.LogInformation(
                "Disabled plugin {Id} ({Name}); ALC remains loaded inert until process restart.",
                id, loaded.Name);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Calls IAutoNatePlugin.Cleanup(context) for the given plugin row. Used by
    // PluginManagementService.DeleteAsync to give the plugin a chance to remove
    // any artifacts it created outside the host's automatic teardown paths
    // (record types, app-level menu rows it owns explicitly, files in shared
    // folders, etc.). Runs whether the plugin is currently enabled or not — we
    // load the assembly into a transient ALC just for this call so the
    // semantics are identical in both cases.
    //
    // Errors are logged and swallowed; cleanup failure must not block the
    // delete that called us.
    public async Task CleanupAsync(Plugin row, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var folder = Path.Combine(_pluginRoot, row.Id.ToString("D"));
            var entryPath = Path.Combine(folder, row.EntryAssembly);
            if (!File.Exists(entryPath))
            {
                _log.LogWarning(
                    "Cleanup skipped for plugin {Id}: entry assembly not found at '{Path}'.",
                    row.Id, entryPath);
                return;
            }

            var alc = new PluginAssemblyLoadContext(entryPath);
            ScopedHookRegistrar? scoped = null;
            try
            {
                var assembly = alc.LoadFromAssemblyPath(entryPath);
                Type? pluginType;
                if (!string.IsNullOrWhiteSpace(row.EntryType))
                {
                    pluginType = assembly.GetType(row.EntryType, throwOnError: false);
                }
                else
                {
                    pluginType = FindSinglePluginType(assembly);
                }
                if (pluginType is null)
                {
                    _log.LogWarning(
                        "Cleanup skipped for plugin {Id}: no IAutoNatePlugin type resolved.", row.Id);
                    return;
                }

                if (Activator.CreateInstance(pluginType) is not IAutoNatePlugin instance)
                {
                    _log.LogWarning(
                        "Cleanup skipped for plugin {Id}: type '{Type}' could not be activated.",
                        row.Id, pluginType.FullName);
                    return;
                }

                IPluginDataAccess data;
                string contextCode;
                if (!string.IsNullOrEmpty(row.Code)
                    && row.RolePasswordEncrypted is not null
                    && _dataRegistry is not null)
                {
                    data = _dataRegistry.GetOrCreate(row.Code, row.RolePasswordEncrypted);
                    contextCode = row.Code;
                }
                else
                {
                    data = new UnprovisionedPluginDataAccess();
                    contextCode = string.Empty;
                }

                IPluginMenus menus = _dbFactory is not null
                    ? new PluginMenus(_dbFactory, row.Id, _loggerFactory.CreateLogger<PluginMenus>())
                    : new NoopPluginMenus();

                // Wrap the registrar so anything Cleanup() accidentally
                // subscribes to gets dropped immediately afterwards. We don't
                // want a cleanup callback to leak hooks into a plugin that's
                // about to be deleted.
                scoped = new ScopedHookRegistrar(_registrar);
                var context = new PluginContext(row.Id, contextCode, scoped, data, menus, _hostServices);

                try
                {
                    instance.Cleanup(context);
                    _log.LogInformation(
                        "Ran Cleanup() for plugin {Id} ({Name} v{Version}).",
                        row.Id, instance.Name, instance.Version);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex,
                        "Plugin {Id} Cleanup() threw; continuing with delete.", row.Id);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "Failed to load plugin {Id} for Cleanup(); continuing with delete.", row.Id);
            }
            finally
            {
                scoped?.RemoveAllForPlugin();
                alc.Unload();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // Releases the per-plugin NpgsqlDataSource. Called by the management
    // service after DisableAsync (data preserved) and again before delete
    // (so the role can be dropped without dangling pooled connections).
    public Task ReleaseDataSourceAsync(string code) =>
        _dataRegistry?.RemoveAsync(code) ?? Task.CompletedTask;

    // Removes every menu_items row tagged with this plugin's id. Called by
    // the management service on disable so the plugin's items vanish from
    // the sidebar; on delete, the FK CASCADE handles cleanup but calling
    // this first is harmless and makes the order of operations explicit.
    public async Task DeletePluginMenuItemsAsync(Guid pluginId, CancellationToken ct)
    {
        if (_dbFactory is null) return;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }
        var removed = PluginMenus.DeleteAllForPlugin(connection, pluginId);
        if (removed > 0)
        {
            _log.LogInformation("Removed {Count} menu item(s) registered by plugin {PluginId}.", removed, pluginId);
        }
    }

    // Ingest <pluginFolder>/PageTemplates/*.template files into
    // public.page_templates as plugin-owned rows. The .template file extension
    // is reserved for plugin JSX templates; the file name (without extension)
    // becomes the template key. Two placeholders are substituted before the
    // JSX is persisted so the plugin's source can address its own host
    // endpoints without knowing its provisioned code at build time:
    //   {{pluginCode}} -> the plugin's 8-char code
    //   {{pluginId}}   -> the plugin's UUID
    //
    // Idempotent: the row's `created_by_plugin_id` makes ownership explicit,
    // which lets us safely UPSERT (and never trample built-in or other-plugin
    // templates with the same key). Templates the plugin previously shipped
    // but no longer carries are deleted — keeps the list in sync with the zip.
    private async Task SyncPluginPageTemplatesAsync(Plugin row, string folder, CancellationToken ct)
    {
        if (_dbFactory is null) return;

        var dir = Path.Combine(folder, "PageTemplates");
        var files = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.template")
                .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
                .ToList()
            : new List<string>();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var keepKeys = new HashSet<string>(StringComparer.Ordinal);
        var pluginCode = row.Code ?? string.Empty;
        var pluginIdString = row.Id.ToString();

        foreach (var file in files)
        {
            var key = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(key))
            {
                _log.LogWarning("Plugin {Id} skipping template file with empty stem: {Path}", row.Id, file);
                continue;
            }

            var content = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            content = content
                .Replace("{{pluginCode}}", pluginCode, StringComparison.Ordinal)
                .Replace("{{pluginId}}", pluginIdString, StringComparison.Ordinal);

            // Default path is namespaced by code so two plugins can't collide
            // on the unique default_path constraint. Lower-cased for stability.
            var defaultPath = string.IsNullOrEmpty(pluginCode)
                ? $"/plugins/_unprovisioned/{key.ToLowerInvariant()}"
                : $"/plugins/{pluginCode}/{key.ToLowerInvariant()}";
            keepKeys.Add(key);

            var existing = await db.PageTemplates
                .FirstOrDefaultAsync(t => t.Key == key, ct)
                .ConfigureAwait(false);
            var now = DateTime.UtcNow;
            if (existing is null)
            {
                db.PageTemplates.Add(new Persistence.Scaffolded.PageTemplate
                {
                    Id = Guid.NewGuid(),
                    Key = key,
                    Name = key,
                    Description = null,
                    DefaultPath = defaultPath,
                    IsEnabled = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    CreatedByPluginId = row.Id,
                    ContentType = "jsx",
                    Content = content,
                });
                _log.LogInformation(
                    "Plugin {Id} registered page template '{Key}' (default path '{DefaultPath}').",
                    row.Id, key, defaultPath);
            }
            else if (existing.CreatedByPluginId == row.Id)
            {
                existing.Name = string.IsNullOrEmpty(existing.Name) ? key : existing.Name;
                existing.DefaultPath = defaultPath;
                existing.IsEnabled = true;
                existing.ContentType = "jsx";
                existing.Content = content;
                existing.UpdatedAtUtc = now;
            }
            else
            {
                _log.LogWarning(
                    "Plugin {Id} cannot register page template '{Key}' — key is already owned by {Owner}.",
                    row.Id, key, existing.CreatedByPluginId?.ToString() ?? "the host");
            }
        }

        // Sweep stale plugin templates whose source file is gone.
        var stale = await db.PageTemplates
            .Where(t => t.CreatedByPluginId == row.Id && !keepKeys.Contains(t.Key))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (stale.Count > 0)
        {
            db.PageTemplates.RemoveRange(stale);
            _log.LogInformation(
                "Plugin {Id} removed {Count} stale page template(s) no longer in the zip: {Keys}",
                row.Id, stale.Count, string.Join(", ", stale.Select(s => s.Key)));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> TryDeleteFilesAsync(Guid id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Caller is responsible for already having disabled. We just nuke
            // the directory; if it fails (Windows file lock on the loaded
            // ALC's dlls), the caller marks DeletedPending and we retry at
            // next startup.
            var folder = Path.Combine(_pluginRoot, id.ToString("D"));
            if (!Directory.Exists(folder)) return true;

            try
            {
                Directory.Delete(folder, recursive: true);
                return true;
            }
            catch (IOException ex)
            {
                _log.LogWarning(ex, "Could not delete plugin folder {Folder} (likely file lock); will retry at next startup.", folder);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.LogWarning(ex, "Could not delete plugin folder {Folder} (access denied); will retry at next startup.", folder);
                return false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Type? FindSinglePluginType(Assembly assembly)
    {
        Type? found = null;
        foreach (var t in assembly.GetTypes())
        {
            if (!typeof(IAutoNatePlugin).IsAssignableFrom(t)) continue;
            if (t.IsAbstract || t.IsInterface) continue;
            if (found is not null) return null; // ambiguous
            found = t;
        }
        return found;
    }
}
