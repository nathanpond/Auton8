using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace AutoNate.Web.Endpoints;

/// <summary>
/// How current the executions view is, and whether it is still updating (#594).
/// </summary>
/// <remarks>
/// <para>
/// Making the cache the read model trades absolute freshness for a single source
/// of truth. That trade is only honest if a user can see it — otherwise the page
/// shows a moment that has passed and presents it as now.
/// </para>
/// <para>
/// <b>Two conditions, not one.</b> "Updated a minute ago" and "not updating" are
/// different things to someone watching a stuck process, and the row alone cannot
/// tell them apart: <c>last_sync_at</c> advances only when a tick <i>succeeds</i>,
/// so a stalled feed and a quiet system look identical. The feed's heartbeat is
/// what separates them.
/// </para>
/// </remarks>
public sealed record ExecutionFreshnessDto(
    DateTimeOffset? AsOfUtc,
    DateTimeOffset? LastPolledAtUtc,
    bool IsUpdating,
    int FreshnessTargetSeconds,
    int PollIntervalSeconds);

public sealed class ExecutionFreshnessService
{
    private readonly IAuthorizer _authorizer;
    private readonly IProjectionWatermarkStore _watermarks;
    private readonly FlowableCacheOptions _options;
    private readonly TimeProvider _time;

    public ExecutionFreshnessService(
        IAuthorizer authorizer,
        IProjectionWatermarkStore watermarks,
        IOptions<FlowableCacheOptions> options,
        TimeProvider time)
    {
        _authorizer = authorizer;
        _watermarks = watermarks;
        _options = options.Value;
        _time = time;
    }

    /// <summary>The feed whose heartbeat says whether executions are updating.</summary>
    public const string ExecutionFeedName = "flowable.exec.poll";

    public async Task<ExecutionFreshnessDto> GetAsync(
        AutoNateDbContext db, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        // Over the AUTHORIZED set, through the same path the list uses -- so the
        // number describes what this actor can actually see rather than the table.
        var rows = await ExecutionListQuery.BuildAsync(
            db, _authorizer, actor, search: null, status: null, workflowModelId: null,
            cancellationToken);

        // THE OLDEST sync time, not the newest. It bounds how stale anything on
        // screen could be; the newest would describe one lucky row and quietly
        // overstate how current the view is.
        var asOf = await rows
            .Select(r => (DateTime?)r.LastSyncAtUtc)
            .MinAsync(cancellationToken);

        var lastPolled = await _watermarks.GetAsync(ExecutionFeedName, cancellationToken);

        var pollInterval = _options.ExecutionPollInterval;
        var staleAfter = pollInterval * Math.Max(1, _options.StaleFeedIntervalMultiplier);

        // No heartbeat at all reads as NOT updating. That is the honest answer on a
        // process that has never completed a sweep -- a fresh deployment, or a feed
        // that has failed every tick since boot. Defaulting to "updating" would
        // make the worst case look like the best one.
        var isUpdating = lastPolled is { } last
                         && _time.GetUtcNow() - last <= staleAfter;

        return new ExecutionFreshnessDto(
            AsOfUtc: asOf is { } a ? new DateTimeOffset(DateTime.SpecifyKind(a, DateTimeKind.Utc)) : null,
            LastPolledAtUtc: lastPolled,
            IsUpdating: isUpdating,
            FreshnessTargetSeconds: (int)_options.ReadThroughFreshness.TotalSeconds,
            PollIntervalSeconds: (int)pollInterval.TotalSeconds);
    }
}
