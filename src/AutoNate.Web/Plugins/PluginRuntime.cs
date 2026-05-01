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
