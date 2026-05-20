using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Records;

// In-memory cache for resolving record-type Guid -> ShortCode at runtime.
//
// Used by WorkflowSignalDispatcher (P2.4) to translate a payload's
// `recordTypeId` into the shortcode form that signal start events filter on
// (`flowable:recordTypeShortCodes`). A cache makes sense because the
// dispatcher fires per inbound bus message and a DB hit per signal would be
// wasteful — record types change rarely.
//
// Refresh strategy:
//   - Initial load: RecordTypeShortCodeCacheInitializer (IHostedService)
//     calls RefreshAsync at app start, before any signals are dispatched.
//   - Mutations: same initializer subscribes a HookPoints.AuditEventPublished
//     handler that watches the 4 record-type lifecycle event types
//     (created / updated / archived / restored) and re-refreshes when one
//     fires. RefreshAsync is internally serialized via _refreshLock, so
//     bursts coalesce automatically.
public sealed class RecordTypeShortCodeCache(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ILogger<RecordTypeShortCodeCache> logger)
    : IRecordTypeShortCodeResolver
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbContextFactory = dbContextFactory;
    private readonly ILogger<RecordTypeShortCodeCache> _logger = logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private IReadOnlyDictionary<Guid, string> _byId =
        new Dictionary<Guid, string>();
    private IReadOnlyDictionary<string, Guid> _byShortCode =
        new Dictionary<string, Guid>(StringComparer.Ordinal);

    public bool TryGetShortCode(Guid recordTypeId, out string shortCode)
    {
        if (_byId.TryGetValue(recordTypeId, out var value))
        {
            shortCode = value;
            return true;
        }
        shortCode = string.Empty;
        return false;
    }

    public IReadOnlyDictionary<string, Guid> ShortCodeToId => _byShortCode;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            await using var dbContext = await _dbContextFactory
                .CreateDbContextAsync(cancellationToken);

            var rows = await dbContext.RecordTypes
                .AsNoTracking()
                .Select(rt => new { rt.Id, rt.ShortCode })
                .ToListAsync(cancellationToken);

            var populated = rows
                .Where(r => !string.IsNullOrWhiteSpace(r.ShortCode))
                .ToArray();

            _byId = populated.ToDictionary(r => r.Id, r => r.ShortCode);
            _byShortCode = populated.ToDictionary(
                r => r.ShortCode, r => r.Id, StringComparer.Ordinal);

            _logger.LogInformation(
                "Record-type short-code cache refreshed: {Count} entries.",
                _byId.Count);
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}

// Populates the cache once at app start (mirroring DaprStreamingSubscriber.
// StartAsync, which refreshes IWorkflowSignalRegistry before subscribing —
// runtime consumers can assume the cache is hot by the time they take a
// dependency on it) AND keeps it fresh by subscribing to record-type
// lifecycle audit events.
public sealed class RecordTypeShortCodeCacheInitializer(
    RecordTypeShortCodeCache cache,
    IHookRegistrar hookRegistrar,
    ILogger<RecordTypeShortCodeCacheInitializer> logger) : IHostedService
{
    private readonly RecordTypeShortCodeCache _cache = cache;
    private readonly IHookRegistrar _hookRegistrar = hookRegistrar;
    private readonly ILogger<RecordTypeShortCodeCacheInitializer> _logger = logger;
    private HookHandle? _hookHandle;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _cache.RefreshAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Don't fail startup over the cache: TryGetShortCode will simply
            // return false until the next refresh. P2.4's dispatcher treats
            // an unresolvable recordTypeId as "no filter match".
            _logger.LogError(ex, "Initial record-type short-code cache refresh failed.");
        }

        // Stay fresh on schema mutations. Action handlers receive the
        // AuditEventNotification as args[0] from AuditEventPublisher's
        // actionHub.DoAsync call (see AuditEventPublisher.cs:98-104).
        _hookHandle = _hookRegistrar.AddActionAsync(
            HookPoints.AuditEventPublished,
            priority: 100,
            async (args, ct) =>
            {
                if (args.Length == 0 || args[0] is not AuditEventNotification notification) return;
                if (!IsRecordTypeLifecycleEvent(notification.EventType)) return;
                try
                {
                    await _cache.RefreshAsync(ct);
                }
                catch (Exception ex)
                {
                    // Refresh failures are not catastrophic; the next
                    // mutation (or the next process restart) will retry.
                    _logger.LogWarning(ex,
                        "Record-type short-code cache refresh failed after {EventType}.",
                        notification.EventType);
                }
            });
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_hookHandle is { } handle)
        {
            _hookRegistrar.RemoveAction(handle);
            _hookHandle = null;
        }
        return Task.CompletedTask;
    }

    private static bool IsRecordTypeLifecycleEvent(string eventType) => eventType switch
    {
        RecordSchemaEventTypes.RecordTypeCreated => true,
        RecordSchemaEventTypes.RecordTypeUpdated => true,
        RecordSchemaEventTypes.RecordTypeArchived => true,
        RecordSchemaEventTypes.RecordTypeRestored => true,
        _ => false
    };
}
