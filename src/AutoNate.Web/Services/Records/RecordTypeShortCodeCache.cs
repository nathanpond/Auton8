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
//   - Mutations: TODO — wire RefreshAsync to the
//     record-type.created/updated/archived/restored audit events. The most
//     likely seam is an IActionHandler subscribed to
//     HookPoints.AuditEventPublished (see AuditEventPublisher.cs:98-104) that
//     filters on those EventTypes and calls RefreshAsync. Today the cache is
//     read-mostly and stale entries only matter for newly-created record
//     types being signalled before the next process restart, which is
//     unlikely in practice. Will be added when a real staleness concern
//     surfaces.
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

            _byId = rows
                .Where(r => !string.IsNullOrWhiteSpace(r.ShortCode))
                .ToDictionary(r => r.Id, r => r.ShortCode);

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

// Populates the cache once at app start. Mirrors the pattern used by
// DaprStreamingSubscriber.StartAsync (which refreshes IWorkflowSignalRegistry
// before subscribing) — runtime consumers can assume the cache is hot by the
// time they take a dependency on it.
public sealed class RecordTypeShortCodeCacheInitializer(
    RecordTypeShortCodeCache cache,
    ILogger<RecordTypeShortCodeCacheInitializer> logger) : IHostedService
{
    private readonly RecordTypeShortCodeCache _cache = cache;
    private readonly ILogger<RecordTypeShortCodeCacheInitializer> _logger = logger;

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
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
