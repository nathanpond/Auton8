using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Plugins;

// Boot-time orchestration:
//   1) Sweep orphan directories under plugins/ that no DB row points at.
//   2) Process DeletedPending rows: try to delete files; if the directory
//      goes, hard-delete the row.
//   3) Load every Status=Enabled plugin via PluginRuntime. Failure flips the
//      row to Disabled with last_error populated, so admins can see + retry.
internal sealed class PluginHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly PluginRuntime _runtime;
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly PluginOptions _options;
    private readonly ILogger<PluginHostedService> _log;

    public PluginHostedService(
        IServiceProvider services,
        PluginRuntime runtime,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IOptions<PluginOptions> options,
        ILogger<PluginHostedService> log)
    {
        _services = services;
        _runtime = runtime;
        _dbFactory = dbFactory;
        _options = options.Value;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_runtime.PluginRoot);

        try
        {
            await SweepOrphanFoldersAsync(cancellationToken);
            await ProcessDeletedPendingAsync(cancellationToken);
            await LoadEnabledPluginsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Plugin host startup failed.");
            if (_options.FailFastOnStartup) throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SweepOrphanFoldersAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var knownIds = (await db.Plugins.AsNoTracking().Select(p => p.Id).ToListAsync(ct))
            .ToHashSet();

        foreach (var dir in Directory.EnumerateDirectories(_runtime.PluginRoot))
        {
            var name = Path.GetFileName(dir);
            if (!Guid.TryParse(name, out var folderId) || !knownIds.Contains(folderId))
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    _log.LogInformation("Removed orphan plugin folder {Folder}.", dir);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Could not remove orphan plugin folder {Folder}.", dir);
                }
            }
        }
    }

    private async Task ProcessDeletedPendingAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Plugins
            .Where(p => p.Status == (int)PluginStatus.DeletedPending)
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            var folder = Path.Combine(_runtime.PluginRoot, row.Id.ToString("D"));
            var canRemove = !Directory.Exists(folder);
            if (!canRemove)
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                    canRemove = true;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex,
                        "DeletedPending plugin {Id} folder still locked — leaving for next attempt.", row.Id);
                }
            }
            if (canRemove)
            {
                db.Plugins.Remove(row);
            }
        }

        if (rows.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task LoadEnabledPluginsAsync(CancellationToken ct)
    {
        List<Plugin> enabledRows;
        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            enabledRows = await db.Plugins
                .Where(p => p.Status == (int)PluginStatus.Enabled)
                .ToListAsync(ct);
        }

        foreach (var row in enabledRows)
        {
            var result = await _runtime.EnableAsync(row, ct);
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var tracked = await db.Plugins.FirstAsync(p => p.Id == row.Id, ct);
            if (result.Success)
            {
                tracked.LastEnabledAt = DateTime.UtcNow;
                tracked.LastError = null;
            }
            else
            {
                tracked.Status = (int)PluginStatus.Disabled;
                tracked.LastError = result.ErrorMessage;
                _log.LogError(
                    "Plugin {Id} ({Name}) failed to enable at startup: {Error}",
                    row.Id, row.Name, result.ErrorMessage);
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
