using System.IO.Compression;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.ApplicationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Plugins;

internal sealed class PluginManagementService : IPluginManagementService
{
    public const string SourceAppId = "autonate.web";

    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly PluginRuntime _runtime;
    private readonly IApplicationEventPublisher _events;
    private readonly PluginOptions _options;
    private readonly PluginSchemaProvisioner _provisioner;
    private readonly ILogger<PluginManagementService> _log;

    public PluginManagementService(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        PluginRuntime runtime,
        IApplicationEventPublisher events,
        IOptions<PluginOptions> options,
        PluginSchemaProvisioner provisioner,
        ILogger<PluginManagementService> log)
    {
        _dbFactory = dbFactory;
        _runtime = runtime;
        _events = events;
        _options = options.Value;
        _provisioner = provisioner;
        _log = log;
    }

    public async Task<IReadOnlyList<PluginListItem>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Plugins
            .AsNoTracking()
            .Where(p => p.Status != (int)PluginStatus.DeletedPending)
            .OrderByDescending(p => p.UploadedAt)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<PluginListItem?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Plugins.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return row is null ? null : ToDto(row);
    }

    public async Task<PluginUploadOutcome> UploadAsync(Stream zipStream, Guid actorUserId, CancellationToken ct = default)
    {
        // Stream the upload to a temp file so the validator can scan it
        // without loading the whole archive into memory; ZipFile.OpenRead
        // expects a path.
        var tempZip = Path.Combine(Path.GetTempPath(), $"autonate-plugin-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var fs = File.Create(tempZip))
            {
                await zipStream.CopyToAsync(fs, ct);
            }

            var validation = PluginUploadValidator.Validate(tempZip, _options.MaxUploadBytes);
            if (!validation.Success)
            {
                return new(false, null, validation.ErrorCode, validation.ErrorMessage);
            }
            var manifest = validation.Manifest!;

            var id = Guid.NewGuid();
            var folder = Path.Combine(_runtime.PluginRoot, id.ToString("D"));
            Directory.CreateDirectory(folder);
            try
            {
                ZipFile.ExtractToDirectory(tempZip, folder, overwriteFiles: true);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to extract plugin zip to {Folder}; rolling back.", folder);
                TryDeleteFolder(folder);
                return new(false, null, "extract_failed", ex.Message);
            }

            // Provision the per-plugin DB role and schema before persisting the
            // row so a failed provision doesn't leave a code-less plugin around.
            // If the row insert below fails we tear down the schema explicitly.
            PluginProvisioningResult provisioning;
            try
            {
                provisioning = await _provisioner.ProvisionAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to provision plugin schema; rolling back.");
                TryDeleteFolder(folder);
                return new(false, null, "provision_failed", ex.Message);
            }

            var row = new Plugin
            {
                Id = id,
                Name = manifest.Name,
                Version = manifest.Version,
                EntryAssembly = manifest.EntryAssembly,
                EntryType = manifest.EntryType,
                Status = (int)PluginStatus.Disabled,
                UploadedAt = DateTime.UtcNow,
                UploadedBy = actorUserId,
                Code = provisioning.Code,
                RolePasswordEncrypted = provisioning.EncryptedPassword,
            };

            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                db.Plugins.Add(row);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to persist plugin row; tearing down schema {Code}.", provisioning.Code);
                TryDeleteFolder(folder);
                try { await _provisioner.TeardownAsync(provisioning.Code, ct); }
                catch (Exception teardownEx)
                {
                    _log.LogError(teardownEx, "Schema teardown after row-insert failure also failed for {Code}.", provisioning.Code);
                }
                return new(false, null, "row_insert_failed", ex.Message);
            }

            await PublishAsync(ApplicationEventTypes.PluginUploaded, row, actorUserId, errorMessage: null, ct);

            return new(true, ToDto(row), null, null);
        }
        finally
        {
            TryDeleteFile(tempZip);
        }
    }

    public async Task<PluginActionOutcome> EnableAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        Plugin? row;
        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            row = await db.Plugins.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (row is null) return new(false, null, "not_found", "Plugin not found.");
            if (row.Status == (int)PluginStatus.DeletedPending)
            {
                return new(false, null, "deleted", "Plugin is pending deletion.");
            }
        }

        var enableResult = await _runtime.EnableAsync(row, ct);

        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            var tracked = await db.Plugins.FirstAsync(p => p.Id == id, ct);
            if (enableResult.Success)
            {
                tracked.Status = (int)PluginStatus.Enabled;
                tracked.LastEnabledAt = DateTime.UtcNow;
                tracked.LastError = null;
            }
            else
            {
                tracked.Status = (int)PluginStatus.Disabled;
                tracked.LastError = enableResult.ErrorMessage;
            }
            await db.SaveChangesAsync(ct);
            row = tracked;
        }

        if (enableResult.Success)
        {
            await PublishAsync(ApplicationEventTypes.PluginEnabled, row, actorUserId, errorMessage: null, ct);
            return new(true, ToDto(row), null, null);
        }

        await PublishAsync(ApplicationEventTypes.PluginEnableFailed, row, actorUserId, enableResult.ErrorMessage, ct);
        return new(false, ToDto(row), "enable_failed", enableResult.ErrorMessage);
    }

    public async Task<PluginActionOutcome> DisableAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        Plugin row;
        await _runtime.DisableAsync(id, ct);
        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            var tracked = await db.Plugins.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (tracked is null) return new(false, null, "not_found", "Plugin not found.");
            tracked.Status = (int)PluginStatus.Disabled;
            tracked.LastDisabledAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            row = tracked;
        }

        // Drop the per-plugin NpgsqlDataSource so its pooled connections close.
        // The schema and role remain — disable is purely a code-side operation.
        if (!string.IsNullOrEmpty(row.Code))
        {
            await _runtime.ReleaseDataSourceAsync(row.Code);
        }

        // Sweep menu items the plugin registered through IPluginMenus. Plugin
        // re-creates them on the next enable inside Configure(); the FK
        // CASCADE on delete handles the case where the plugin is removed
        // entirely, but disable preserves the row so we sweep explicitly.
        await _runtime.DeletePluginMenuItemsAsync(id, ct);

        await PublishAsync(ApplicationEventTypes.PluginDisabled, row, actorUserId, errorMessage: null, ct);
        return new(true, ToDto(row), null, null);
    }

    public async Task<PluginActionOutcome> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        // Snapshot the row up-front so cleanup can run against authoritative
        // state regardless of subsequent disable/teardown side-effects.
        Plugin? snapshotForEvent;
        string? code;
        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            var tracked = await db.Plugins.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (tracked is null) return new(false, null, "not_found", "Plugin not found.");
            snapshotForEvent = tracked;
            code = tracked.Code;
        }

        // Drop hooks first if currently loaded. Idempotent if already disabled.
        await _runtime.DisableAsync(id, ct);

        // Give the plugin a chance to remove artifacts it created outside the
        // host's automatic teardown — record types, app-level menu rows it
        // wants to sweep explicitly, files in shared folders. Runs even if
        // the plugin was disabled at delete time (the runtime spins up a
        // transient ALC just for this call). Failures are logged inside
        // CleanupAsync; we never let cleanup block a delete.
        await _runtime.CleanupAsync(snapshotForEvent, ct);

        // Drop schema/role next; the database side has no file locks so this
        // can always run to completion immediately, decoupling data lifecycle
        // from on-disk file lifecycle. If files remain (Windows lock), we
        // fall through to DeletedPending and only the file delete is retried.
        if (!string.IsNullOrEmpty(code))
        {
            // Close the plugin's NpgsqlDataSource first so pooled connections
            // don't hold the role and block DROP ROLE.
            await _runtime.ReleaseDataSourceAsync(code);
            try
            {
                await _provisioner.TeardownAsync(code, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to tear down plugin schema/role for {Code}; continuing with file delete.", code);
            }
        }

        var filesGone = await _runtime.TryDeleteFilesAsync(id, ct);

        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            var tracked = await db.Plugins.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (tracked is null)
            {
                // Race with another delete; treat as success.
                await PublishAsync(ApplicationEventTypes.PluginDeleted, snapshotForEvent, actorUserId, errorMessage: null, ct);
                return new(true, null, null, null);
            }

            if (filesGone)
            {
                db.Plugins.Remove(tracked);
                await db.SaveChangesAsync(ct);
                await PublishAsync(ApplicationEventTypes.PluginDeleted, snapshotForEvent, actorUserId, errorMessage: null, ct);
                return new(true, null, null, null);
            }

            tracked.Status = (int)PluginStatus.DeletedPending;
            await db.SaveChangesAsync(ct);
            await PublishAsync(ApplicationEventTypes.PluginDeleted, snapshotForEvent, actorUserId,
                errorMessage: "files-locked: deletion deferred to next startup", ct);
            return new(true, null, null, null);
        }
    }

    private async Task PublishAsync(string eventType, Plugin row, Guid? actorUserId, string? errorMessage, CancellationToken ct)
    {
        var envelope = new ApplicationEventEnvelope(
            EventId: Guid.NewGuid(),
            EventType: eventType,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            ActorUserId: actorUserId,
            Payload: new
            {
                pluginId = row.Id,
                name = row.Name,
                version = row.Version,
                errorMessage = errorMessage,
            },
            SourceAppId: SourceAppId);
        await _events.PublishAsync(envelope, ct);
    }

    private static PluginListItem ToDto(Plugin row) => new(
        row.Id,
        row.Name,
        row.Version,
        (PluginStatus)row.Status,
        row.UploadedAt,
        row.UploadedBy,
        row.LastEnabledAt,
        row.LastDisabledAt,
        row.LastError);

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }

    private static void TryDeleteFolder(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* best-effort */ }
    }
}
