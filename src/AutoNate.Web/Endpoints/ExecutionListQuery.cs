using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Flowable;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AutoNate.Web.Endpoints;

/// <summary>
/// The executions list, as one SQL query over <c>workflow_execution_cache</c> (#108).
/// </summary>
/// <remarks>
/// <para>
/// The list used to fetch from Flowable, authorize in memory, then slice the page
/// client-side. Two problems ran together in that: the fetch was capped at 200 so
/// the list <b>silently truncated</b>, and authorization ran after the fetch, so a
/// page was sliced from a set filtered by a different evaluator than a SQL query
/// would use. Filtering after paging is what makes a fast list wrong.
/// </para>
/// </remarks>
public static class ExecutionListQuery
{
    /// <summary>
    /// The API's status vocabulary, which is not the cache's (#108).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cache stores the projection's normalized values — <c>active</c>,
    /// <c>completed</c>, <c>cancelled</c>, <c>suspended</c>, <c>terminated</c>.
    /// The SPA compares against <c>"Running"</c>, <c>"Complete"</c>,
    /// <c>"Cancelled"</c> and <c>"Errored"</c>
    /// (<c>WorkflowExecutions.tsx:191-194</c>, <c>:283</c>). Serving the list from
    /// the cache without translating would leave every status count at zero and
    /// every badge wrong, and nothing would error — the page would just quietly
    /// stop meaning anything.
    /// </para>
    /// <para>
    /// <c>suspended</c> maps to <c>Running</c> and <c>terminated</c> to
    /// <c>Cancelled</c> because the API vocabulary has no member for either. That
    /// loses a distinction the cache can make; it is recorded here rather than
    /// hidden, and widening the API's vocabulary is a UI change, not this story.
    /// </para>
    /// </remarks>
    public const string Running = "Running";
    public const string Complete = "Complete";
    public const string Cancelled = "Cancelled";
    public const string Errored = "Errored";

    /// <summary>
    /// Builds the authorized, filtered query. Paging and counting are the
    /// caller's, so `/` and `/page` share one definition of "which rows".
    /// </summary>
    public static async Task<IQueryable<ExecutionListRow>> BuildAsync(
        AutoNateDbContext db,
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string? search,
        string? status,
        string? workflowModelId,
        CancellationToken cancellationToken)
    {
        // Authorization is pushed INTO the query (#108). The compiler this uses is
        // the one #574-#577 made agree with the in-memory evaluator, which is why
        // moving the decision here changes where it is made and not what it
        // decides.
        var authorized = await authorizer.FilterQueryAsync(
            db, actor, EntityKinds.WorkflowExecution, Actions.View,
            db.WorkflowExecutionCache.AsNoTracking(), cancellationToken);

        var rows = authorized.Select(c => new ExecutionListRow
        {
            Id = c.FlowableInstanceId,
            Name = c.Name,
            WorkflowModelName = c.WorkflowModelName,
            StartedAtUtc = c.StartTime,
            ProcessDefinitionId = c.ProcessDefinitionId,
            StartUserId = c.StartedBy,
            CurrentStep = c.CurrentActivityName,
            CacheStatus = c.Status,

            // The Errored overlay, as an EXISTS rather than a second round trip.
            // ix_workflow_execution_errors_process_instance_id covers it.
            IsErrored = db.WorkflowExecutionErrors
                .Any(e => e.ProcessInstanceId == c.FlowableInstanceId),

            // The "Last activity" column the table renders. The cache has no such
            // field, and EndTime ?? StartTime would show a RUNNING instance its own
            // start time -- exactly the rows an operator watches. The event log
            // carries the same signal the live path read from activity history, and
            // ix_workflow_event_log_instance_time (flowable_instance_id,
            // event_time DESC) is already there for it.
            LastActivityAtUtc = db.WorkflowEventLogCache
                .Where(e => e.FlowableInstanceId == c.FlowableInstanceId)
                .Max(e => (DateTime?)e.EventTime)
        });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = $"%{search.Trim()}%";
            rows = rows.Where(r =>
                (r.Name != null && EF.Functions.ILike(r.Name, needle))
                || EF.Functions.ILike(r.Id, needle)
                || (r.WorkflowModelName != null && EF.Functions.ILike(r.WorkflowModelName, needle)));
        }

        if (!string.IsNullOrWhiteSpace(workflowModelId))
        {
            var model = workflowModelId.Trim();
            rows = rows.Where(r => r.WorkflowModelName != null
                                && EF.Functions.ILike(r.WorkflowModelName, model));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            // Compared against the API vocabulary the caller speaks, translated
            // into cache terms here -- so the filter and the rendered badge cannot
            // disagree about what "Running" means.
            var wanted = status.Trim();
            rows = wanted switch
            {
                Errored => rows.Where(r => r.IsErrored
                                        && r.CacheStatus != WorkflowExecutionStatuses.Cancelled
                                        && r.CacheStatus != WorkflowExecutionStatuses.Terminated),
                Cancelled => rows.Where(r => r.CacheStatus == WorkflowExecutionStatuses.Cancelled
                                          || r.CacheStatus == WorkflowExecutionStatuses.Terminated),
                Complete => rows.Where(r => r.CacheStatus == WorkflowExecutionStatuses.Completed
                                         && !r.IsErrored),
                Running => rows.Where(r => (r.CacheStatus == WorkflowExecutionStatuses.Active
                                         || r.CacheStatus == WorkflowExecutionStatuses.Suspended)
                                        && !r.IsErrored),
                _ => rows.Where(_ => false)
            };
        }

        return rows;
    }

    /// <summary>
    /// Ordering, with the tie-break stated rather than inherited (#108).
    /// </summary>
    /// <remarks>
    /// <para>The in-memory list tie-broke on <c>Id</c> with
    /// <c>StringComparer.Ordinal</c>. Postgres orders text by the database's
    /// collation, which is a server setting: measured here it reports
    /// <c>en_US.utf8</c> and yet orders <c>B,Z,a,b</c> — identical to <c>C</c> — and
    /// Flowable's ids are lowercase-hex UUIDs where the two agree regardless. So
    /// today it would match by coincidence of this deployment.</para>
    ///
    /// <para><c>COLLATE "C"</c> costs nothing and removes the dependency on a
    /// setting that can differ between a developer's machine, CI and production.
    /// A stable tie-break is what makes paging correct rather than merely fast:
    /// without it a row can appear on two pages or none.</para>
    /// </remarks>
    public static IOrderedQueryable<ExecutionListRow> Order(
        IQueryable<ExecutionListRow> rows, string? sort, bool descending)
    {
        // NB: EF.Functions.Collate only exists inside a query tree. Calling it
        // here to "document intent" throws at runtime -- which it did, as a 500
        // on every list request, until the tests caught it. The collation is
        // applied per key below, where it is actually translated.
        return (sort, descending) switch
        {
            ("name", true) => rows.OrderByDescending(r => r.Name)
                .ThenBy(r => EF.Functions.Collate(r.Id, "C")),
            ("name", false) => rows.OrderBy(r => r.Name)
                .ThenBy(r => EF.Functions.Collate(r.Id, "C")),
            ("workflowModel", true) => rows.OrderByDescending(r => r.WorkflowModelName)
                .ThenBy(r => EF.Functions.Collate(r.Id, "C")),
            ("workflowModel", false) => rows.OrderBy(r => r.WorkflowModelName)
                .ThenBy(r => EF.Functions.Collate(r.Id, "C")),
            ("status", true) => rows.OrderByDescending(r => r.CacheStatus)
                .ThenBy(r => EF.Functions.Collate(r.Id, "C")),
            ("status", false) => rows.OrderBy(r => r.CacheStatus)
                .ThenBy(r => EF.Functions.Collate(r.Id, "C")),
            ("startedAtUtc", false) => rows.OrderBy(r => r.StartedAtUtc)
                .ThenBy(r => EF.Functions.Collate(r.Id, "C")),
            _ => rows.OrderByDescending(r => r.StartedAtUtc)
                .ThenBy(r => EF.Functions.Collate(r.Id, "C"))
        };
    }

    /// <summary>Maps a queried row to the API shape the SPA already consumes.</summary>
    public static WorkflowExecutionSummary ToSummary(ExecutionListRow r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        WorkflowModelName = r.WorkflowModelName,
        StartedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(r.StartedAtUtc, DateTimeKind.Utc)),
        LastActivityAtUtc = r.LastActivityAtUtc is { } last
            ? new DateTimeOffset(DateTime.SpecifyKind(last, DateTimeKind.Utc))
            : new DateTimeOffset(DateTime.SpecifyKind(r.StartedAtUtc, DateTimeKind.Utc)),
        Status = EffectiveStatus(r.CacheStatus, r.IsErrored),
        CurrentStep = r.CurrentStep,
        ProcessDefinitionId = string.IsNullOrWhiteSpace(r.ProcessDefinitionId) ? null : r.ProcessDefinitionId,
        StartUserId = string.IsNullOrWhiteSpace(r.StartUserId) ? null : r.StartUserId
    };

    /// <summary>
    /// Cache status plus the Errored overlay, in the API's vocabulary (#108).
    /// </summary>
    /// <remarks>
    /// The precedence is the live path's, kept deliberately: <b>Cancelled beats
    /// Errored</b>, because operator intent supersedes a stale failure, and
    /// <b>Errored beats Running and Complete</b>, because a process with a failed
    /// job is still actionable but no longer healthy.
    /// </remarks>
    public static string EffectiveStatus(string cacheStatus, bool isErrored) => cacheStatus switch
    {
        WorkflowExecutionStatuses.Cancelled => Cancelled,
        WorkflowExecutionStatuses.Terminated => Cancelled,
        _ when isErrored => Errored,
        WorkflowExecutionStatuses.Completed => Complete,
        _ => Running
    };
}

/// <summary>The shape the SQL query projects, before it becomes the API DTO.</summary>
public sealed class ExecutionListRow
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? WorkflowModelName { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? LastActivityAtUtc { get; set; }
    public string CacheStatus { get; set; } = string.Empty;
    public bool IsErrored { get; set; }
    public string? CurrentStep { get; set; }
    public string? ProcessDefinitionId { get; set; }
    public string? StartUserId { get; set; }
}
