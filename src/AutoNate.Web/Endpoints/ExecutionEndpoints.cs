using System.Security.Claims;
using System.Text.Json;
using System.Xml.Linq;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Models;
using AutoNate.Web.Models.Forms;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Flowable.Cache;
using Microsoft.AspNetCore.Mvc;
using AutoNate.Web.Services.Forms;
using AutoNate.Web.Services.Workflow;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

public static class ExecutionEndpoints
{
    public static IEndpointRouteBuilder MapExecutionEndpoints(this IEndpointRouteBuilder app)
    {
        var executions = app.MapGroup("/api/executions")
            .RequireAuthorization();

        // SERVED BY ONE SQL QUERY OVER THE CACHE (#108).
        //
        // This used to fetch from Flowable, authorize in memory, then slice the
        // page client-side. Two defects rode together in that: the fetch was
        // capped at 200 so the list SILENTLY TRUNCATED (#588 removed the cap),
        // and authorization ran after the fetch, so the page was sliced from a
        // set filtered by a different evaluator than a SQL query would use.
        executions.MapGet("/", async (
            HttpContext http,
            IAuthorizer authorizer,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var rows = await ExecutionListQuery.BuildAsync(
                db, authorizer, http.User, search: null, status: null, workflowModelId: null,
                cancellationToken);

            var items = (await ExecutionListQuery.Order(rows, sort: null, descending: true)
                    .ToListAsync(cancellationToken))
                .Select(ExecutionListQuery.ToSummary)
                .ToArray();

            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionListViewed,
                WorkflowResourceKinds.Execution,
                resource: null,
                details: new { resultCount = items.Length },
                cancellationToken);

            return Results.Ok(items);
        }).AuthorizedInHandler("filters via ExecutionListQuery (WorkflowExecution, View) pushed into SQL");

        // Paged variant. Filtering happens BEFORE paging, which is what makes the
        // page correct rather than merely fast: reversing them slices a page out
        // of the unfiltered set.
        executions.MapGet("/page", async (
            int? page,
            int? pageSize,
            string? q,
            string? sort,
            string? sortDir,
            string? status,
            string? workflowModelId,
            HttpContext http,
            IAuthorizer authorizer,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var rows = await ExecutionListQuery.BuildAsync(
                db, authorizer, http.User, q, status, workflowModelId, cancellationToken);

            // COUNT over the filtered set, without materialising it.
            var totalCount = await rows.CountAsync(cancellationToken);

            var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
            var ordered = ExecutionListQuery.Order(rows, sort, desc);

            var pageIndex = Math.Max(0, page ?? 0);
            var size = pageSize ?? 25;

            var items = size > 0
                ? (await ordered.Skip(pageIndex * size).Take(size).ToListAsync(cancellationToken))
                    .Select(ExecutionListQuery.ToSummary).ToArray()
                : Array.Empty<WorkflowExecutionSummary>();

            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionListViewed,
                WorkflowResourceKinds.Execution,
                resource: null,
                details: new
                {
                    resultCount = items.Length,
                    totalCount,
                    page = pageIndex,
                    pageSize = size,
                    search = q,
                    status,
                    workflowModelId
                },
                cancellationToken);

            return Results.Ok(new { items, totalCount });
        }).AuthorizedInHandler("filters via ExecutionListQuery (WorkflowExecution, View) pushed into SQL");

        // HOW CURRENT THIS VIEW IS, AND WHETHER IT IS STILL UPDATING (#594).
        //
        // A separate endpoint rather than fields on the list, because
        // GET /api/executions returns a bare array and wrapping it would break
        // the SPA, the endpoint tests and the E2E specs for a purely additive
        // signal.
        executions.MapGet("/freshness", async (
            HttpContext http,
            ExecutionFreshnessService freshness,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            CancellationToken cancellationToken) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            return Results.Ok(await freshness.GetAsync(db, http.User, cancellationToken));
        }).RequireKindPermission(EntityKinds.WorkflowExecution, Actions.View);

        executions.MapGet("/{processInstanceId}/diagram", async (
            string processInstanceId,
            IFlowableClient flowable,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var detail = await flowable.GetWorkflowExecutionDiagramDetailAsync(processInstanceId, cancellationToken);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // #218. Show the operator the diagram the author drew, not the one
            // the engine was given.
            //
            // Publish expands elements Flowable cannot run into ones it can, so
            // the deployed resource contains nodes that exist in no stored
            // diagram. Rendering it shows an operator a shape nobody authored,
            // and the ids of those generated nodes highlight nothing.
            //
            // Pinned to the version THIS instance is running. Fetching the latest
            // stored model instead would show an operator a diagram their process
            // never followed the moment anyone republishes, which is worse than
            // showing the expansion.
            detail = await RenderAuthoredDiagramAsync(db, detail, cancellationToken);
            // Project only the three columns the handler actually reads — ErrorStackTrace
            // can be tens of KB and is surfaced on the history endpoint, not here.
            var errorRows = await db.WorkflowExecutionErrors.AsNoTracking()
                .Where(e => e.ProcessInstanceId == processInstanceId)
                .Select(e => new { e.ActivityId, e.ErrorMessage, e.OccurredAtUtc })
                .ToListAsync(cancellationToken);

            // Mapped like every other id surface. A half-mapped diagram
            // highlights nothing and reads as though the process never reached
            // the element.
            var failedActivityIds = errorRows
                .Select(e => MapActivityId(detail, e.ActivityId))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionDiagramViewed,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId },
                details: new { failedActivityCount = failedActivityIds.Count },
                cancellationToken);

            if (errorRows.Count == 0)
            {
                return Results.Ok(detail);
            }

            // Latest non-empty message per activity. We take the freshest error
            // because retries can produce successively different messages and the
            // most recent one is what the operator wants to see in the tooltip.
            var errorMessagesByActivityId = errorRows
                .GroupBy(e => MapActivityId(detail, e.ActivityId), StringComparer.Ordinal)
                .Select(g => new
                {
                    ActivityId = g.Key,
                    Message = g.OrderByDescending(e => e.OccurredAtUtc)
                               .Select(e => e.ErrorMessage)
                               .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
                })
                .Where(x => x.Message != null)
                .ToDictionary(x => x.ActivityId, x => x.Message!, StringComparer.Ordinal);

            return Results.Ok(detail with
            {
                FailedActivityIds = failedActivityIds,
                ErrorMessagesByActivityId = errorMessagesByActivityId
            });
        }).RequirePermission(EntityKinds.WorkflowExecution, Actions.View, "processInstanceId");

        executions.MapGet("/{processInstanceId}/history", async (
            string processInstanceId,
            IFlowableClient flowable,
            IFlowableReadThrough readThrough,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var history = await flowable.GetWorkflowExecutionHistoryAsync(processInstanceId, cancellationToken);

            // #218. The same mapping the diagram gets. History showing an
            // activity id that appears in no diagram the author has ever seen is
            // the same defect one surface over.
            var expansionSources = await flowable.GetExpansionSourceMapAsync(
                processInstanceId, cancellationToken);
            if (expansionSources.Count > 0)
            {
                history = history
                    .Select(e => expansionSources.TryGetValue(e.ActivityId, out var source)
                        ? e with { ActivityId = source }
                        : e)
                    .ToList();
            }
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionHistoryViewed,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId },
                details: new { resultCount = history.Count },
                cancellationToken);

            var completedTaskIds = history
                .Where(e => !string.IsNullOrWhiteSpace(e.TaskId) && e.EndedAtUtc is not null)
                .Select(e => e.TaskId!)
                .Distinct()
                .ToList();

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var completions = completedTaskIds.Count == 0
                ? new Dictionary<string, WorkflowTaskCompletion>()
                : await db.WorkflowTaskCompletions.AsNoTracking()
                    .Where(c => completedTaskIds.Contains(c.TaskId))
                    .ToDictionaryAsync(c => c.TaskId, cancellationToken);

            // Group errors by activityId — multiple retries collapse into a
            // count + latest message stamped onto the matching activity row.
            // Unlike the diagram handler, history surfaces ErrorStackTrace to the SPA,
            // so we materialize the full row here. Don't add a Select(...) projection
            // without first confirming every column accessed below is included.
            var errorRows = await db.WorkflowExecutionErrors.AsNoTracking()
                .Where(e => e.ProcessInstanceId == processInstanceId)
                .OrderBy(e => e.OccurredAtUtc)
                .ToListAsync(cancellationToken);

            // #310. Through the SAME mapping the history rows just went through.
            //
            // These were grouped by the RAW activity id while `history` above was
            // mapped, so the lookup below never matched: the author's gateway came
            // back not marked as errored, and the phantom-row synthesis further
            // down then invented a row carrying `cg__autonateRoute` -- an id in no
            // diagram the author has ever seen.
            //
            // The round-6 guard could not see it because it seeds history rows and
            // no error rows, so this second code path was never entered.
            string MapErrorActivityId(string activityId) =>
                expansionSources.TryGetValue(activityId, out var source) ? source : activityId;

            var errorsByActivity = errorRows
                .GroupBy(e => MapErrorActivityId(e.ActivityId), StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        // Pick the latest row that has either a non-empty message or a
                        // non-empty stack. Surfacing both fields from the SAME row keeps
                        // the operator's mental model honest — message X belongs to
                        // stack Y, not "latest message OR latest stack from possibly
                        // different retries."
                        var latest = g.Reverse()
                            .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.ErrorMessage)
                                              || !string.IsNullOrWhiteSpace(e.ErrorStackTrace));
                        return new
                        {
                            Count = g.Count(),
                            LatestMessage = latest?.ErrorMessage,
                            LatestStackTrace = latest?.ErrorStackTrace
                        };
                    },
                    StringComparer.Ordinal);

            if (completions.Count == 0 && errorsByActivity.Count == 0)
            {
                // #173. Still collapsed. This shortcut skips the enrichment, which
                // is fine -- there is none to do -- but it used to skip the
                // multi-instance collapse with it, so a process whose only
                // interesting feature was a multi-instance activity took the one
                // path that could not show it. Measured: the collapse never ran,
                // and an unconditional `throw` placed inside it was never reached.
                return Results.Ok(await CollapseMultiInstanceAsync(
                    history, processInstanceId, dbFactory, flowable, readThrough, cancellationToken));
            }

            var enriched = history
                .Select(e =>
                {
                    var updated = e;

                    if (!string.IsNullOrWhiteSpace(e.TaskId)
                        && completions.TryGetValue(e.TaskId!, out var completion))
                    {
                        updated = updated with
                        {
                            CompletedByUserId = completion.CompletedByUserId,
                            IsOverride = completion.WasOverride
                        };
                    }

                    if (errorsByActivity.TryGetValue(e.ActivityId, out var errorAgg))
                    {
                        updated = updated with
                        {
                            IsErrored = true,
                            ErrorCount = errorAgg.Count,
                            ErrorMessage = errorAgg.LatestMessage,
                            ErrorStackTrace = errorAgg.LatestStackTrace
                        };
                    }

                    return updated;
                })
                .ToList();

            // Flowable rolls back the failing transaction including its
            // historic-activity-instances write — synchronous script/service
            // tasks that throw never appear in `history`. Synthesize a row
            // for each errored activityId that's missing so the History tab
            // surfaces the failure.
            var historyActivityIds = new HashSet<string>(
                history.Select(e => e.ActivityId),
                StringComparer.Ordinal);

            // #310. Mapped here too, or a generated id that IS present in history
            // under its authored name gets a phantom row synthesized beside it.
            foreach (var errorRow in errorRows.GroupBy(e => MapErrorActivityId(e.ActivityId), StringComparer.Ordinal))
            {
                if (historyActivityIds.Contains(errorRow.Key))
                {
                    continue;
                }

                var first = errorRow.OrderBy(e => e.OccurredAtUtc).First();
                // Pick the latest row that has either a non-empty message or a non-empty
                // stack. Same recency-of-pair rule as the errorsByActivity projection.
                var latest = errorRow.Reverse()
                    .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.ErrorMessage)
                                      || !string.IsNullOrWhiteSpace(e.ErrorStackTrace));
                var nameFromRow = errorRow
                    .Select(e => e.ActivityName)
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

                enriched.Add(new WorkflowExecutionHistoryEvent
                {
                    ActivityId = errorRow.Key,
                    ActivityName = nameFromRow,
                    ActivityType = null,
                    StartedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(first.OccurredAtUtc, DateTimeKind.Utc)),
                    EndedAtUtc = null,
                    DurationMs = null,
                    Assignee = null,
                    TaskId = null,
                    DeleteReason = null,
                    IsErrored = true,
                    ErrorCount = errorRow.Count(),
                    ErrorMessage = latest?.ErrorMessage,
                    ErrorStackTrace = latest?.ErrorStackTrace
                });
            }

            var sorted = enriched
                .OrderBy(e => e.StartedAtUtc ?? DateTimeOffset.MinValue)
                .ToArray();

            // #173. A multi-instance activity collapses to ONE row carrying its
            // progress. Done here rather than in the browser because a process
            // with 500 instances would otherwise put 500 rows on the wire before
            // anything could decide not to show them -- which is the cost the
            // lazy-loading criterion exists to avoid, and it cannot be paid in
            // the SPA.
            var collapsed = await CollapseMultiInstanceAsync(
                sorted, processInstanceId, dbFactory, flowable, readThrough, cancellationToken);

            return Results.Ok(collapsed);
        }).RequirePermission(EntityKinds.WorkflowExecution, Actions.View, "processInstanceId");

        // #173. The instances behind a collapsed row, fetched when it is expanded.
        //
        // A separate route rather than a flag on the one above, so that "do not
        // pay for instances you did not open" is a property of the API rather
        // than a promise about how a caller uses it.
        executions.MapGet("/{processInstanceId}/activities/{activityId}/instances", async (
            string processInstanceId,
            string activityId,
            IFlowableClient flowable,
            IFlowableReadThrough readThrough,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var history = await flowable.GetWorkflowExecutionHistoryAsync(processInstanceId, cancellationToken);

            var instances = history
                .Where(e => string.Equals(e.ActivityId, activityId, StringComparison.Ordinal))
                .OrderBy(e => e.StartedAtUtc ?? DateTimeOffset.MinValue)
                .ThenBy(e => e.TaskId, StringComparer.Ordinal)
                .ToArray();

            // Which collection item each instance was handed. The loop declares the
            // NAME (`flowable:elementVariable`); the engine stores the VALUE as a
            // variable scoped to that instance's own execution -- measured: three
            // instances over ["alice","bob","carol"] produce three `reviewer` rows,
            // one per execution id, and they outlive the activity.
            //
            // Enriched only here, never on the collapsed history: this is the whole
            // reason an operator opens the expansion, and paying for it on every
            // history read is what the lazy-loading criterion exists to prevent.
            //
            // The local is NOT named for the attribute: MultiInstanceReaderAgreement-
            // Tests screens for the spelling by substring, and a variable holding a
            // value the shared reader already returned would read to it as a second
            // reader. Renaming the local is right where allowlisting this method --
            // which maps every execution route -- would have blinded the guard to
            // whatever lands in it next.
            var itemVariableName = await ResolveElementVariableAsync(
                processInstanceId, activityId, dbFactory, readThrough, cancellationToken);

            if (!string.IsNullOrWhiteSpace(itemVariableName))
            {
                var engine = await flowable.GetMultiInstanceEngineStateAsync(
                    processInstanceId, cancellationToken);

                for (var i = 0; i < instances.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(instances[i].ExecutionId)) continue;

                    if (engine.VariablesByExecutionId.TryGetValue(instances[i].ExecutionId!, out var bag)
                        && bag.TryGetValue(itemVariableName!, out var value))
                    {
                        instances[i] = instances[i] with { ElementValue = value };
                    }
                }
            }

            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionHistoryViewed,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId, activityId },
                details: new { resultCount = instances.Length },
                cancellationToken);

            return Results.Ok(instances);
        }).RequirePermission(EntityKinds.WorkflowExecution, Actions.View, "processInstanceId");

        executions.MapGet("/{processInstanceId}/log", async (
            string processInstanceId,
            IFlowableClient flowable,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var log = await flowable.GetWorkflowExecutionLogAsync(processInstanceId, cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionLogViewed,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId },
                details: new { resultCount = log.Count },
                cancellationToken);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // Completer enrichment for task-completed entries.
            var completedTaskIds = log
                .Where(e => e.Kind == "task-completed" && e.Task is not null)
                .Select(e => e.Task!.TaskId)
                .Distinct()
                .ToList();

            var completions = completedTaskIds.Count == 0
                ? new Dictionary<string, WorkflowTaskCompletion>()
                : await db.WorkflowTaskCompletions.AsNoTracking()
                    .Where(c => completedTaskIds.Contains(c.TaskId))
                    .ToDictionaryAsync(c => c.TaskId, cancellationToken);

            // Pull every recorded failure for this process and project each
            // as a chronological "error" log entry — one per retry attempt.
            var errorRows = await db.WorkflowExecutionErrors.AsNoTracking()
                .Where(e => e.ProcessInstanceId == processInstanceId)
                .OrderBy(e => e.OccurredAtUtc)
                .ToListAsync(cancellationToken);

            // Resolve activity names from Flowable's historic-activity-instances
            // — the workflow_execution_errors row often has an empty
            // activity_name (the Flowable extension doesn't reliably populate
            // it today).
            var activityNames = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (errorRows.Count > 0)
            {
                var history = await flowable.GetWorkflowExecutionHistoryAsync(processInstanceId, cancellationToken);
                foreach (var h in history)
                {
                    if (!string.IsNullOrWhiteSpace(h.ActivityId) && !activityNames.ContainsKey(h.ActivityId))
                    {
                        activityNames[h.ActivityId] = h.ActivityName;
                    }
                }
            }

            var errorEntries = errorRows.Select(row => new WorkflowExecutionLogEntry
            {
                Kind = "error",
                OccurredAtUtc = new DateTimeOffset(DateTime.SpecifyKind(row.OccurredAtUtc, DateTimeKind.Utc)),
                Error = new WorkflowExecutionLogError
                {
                    ActivityId = row.ActivityId,
                    ActivityName = !string.IsNullOrWhiteSpace(row.ActivityName)
                        ? row.ActivityName
                        : activityNames.GetValueOrDefault(row.ActivityId),
                    ErrorMessage = string.IsNullOrWhiteSpace(row.ErrorMessage) ? null : row.ErrorMessage,
                    RawFlowableEventType = string.IsNullOrWhiteSpace(row.RawFlowableEventType) ? null : row.RawFlowableEventType
                }
            });

            var merged = log
                .Select(entry =>
                {
                    if (entry.Kind != "task-completed" || entry.Task is null) return entry;
                    if (!completions.TryGetValue(entry.Task.TaskId, out var completion)) return entry;
                    return entry with
                    {
                        Task = entry.Task with
                        {
                            CompletedByUserId = completion.CompletedByUserId,
                            IsOverride = completion.WasOverride
                        }
                    };
                })
                .Concat(errorEntries)
                .OrderBy(e => e.OccurredAtUtc ?? DateTimeOffset.MinValue)
                .ToArray();

            return Results.Ok(merged);
        }).RequirePermission(EntityKinds.WorkflowExecution, Actions.View, "processInstanceId");

        // SERVED FROM THE CACHE (#104), not from a live Flowable call.
        //
        // Two things had to land first, and both were found by this story
        // stopping on its first line:
        //   * #583 -- workflow_execution_cache had no `name` column, and this
        //     DTO carries ProcessInstanceName, which the task modals render;
        //   * #586 -- workflow_task_cache never learned a task finished, so
        //     serving from it would have returned every task the instance ever
        //     had, all labelled active.
        //
        // The instance is resolved through IFlowableReadThrough BEFORE the query:
        // on a cache miss that reads through and populates the row, and the
        // execution projection coalesces that instance's tasks in the same batch
        // (CoalesceTasksOnNewInstance), so a run started seconds ago has its
        // tasks here rather than an empty list. A 404 means the engine says there
        // is no such instance, or there is no cached row and no way to ask.
        executions.MapGet("/{processInstanceId}/tasks", async (
            string processInstanceId,
            IFlowableReadThrough readThrough,
            WorkflowTaskCacheRefresher taskReadThrough,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var instance = await readThrough.GetInstanceAsync(processInstanceId, cancellationToken);
            if (instance is null)
            {
                return Results.NotFound();
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // #604. THIS IS A DETAIL VIEW, so it reads through when the rows are
            // past the same freshness window the instance above just used.
            //
            // Cached alone, it was a minute stale in the one place a caller is
            // watching for change: a timer firing, a boundary event, an ad-hoc
            // activity starting, or the successor to a task they just completed.
            // Measured -- 24 of 493 full-local specs, every one of them waiting
            // 30-45s on a 60s poll.
            var live = await taskReadThrough.ReadThroughOpenTasksAsync(
                processInstanceId, db, cancellationToken);

            // The open-task predicate #586 made load-bearing. Before it, both
            // clauses matched every row.
            var rows = await db.WorkflowTaskCache.AsNoTracking()
                .Where(t => t.FlowableInstanceId == processInstanceId
                         && t.Status == "active"
                         && t.CompletedTime == null)
                .OrderBy(t => t.CreatedTime)
                .ThenBy(t => t.FlowableTaskId)
                .ToListAsync(cancellationToken);

            // The live answer is authoritative for "open right now" when there is
            // one. The cached rows still answer when the window was fresh, or
            // when the engine could not be reached -- and a row the engine no
            // longer lists is filtered out HERE rather than marked complete in
            // the cache, because its absence says it is not open and says nothing
            // about when it finished (#586).
            if (live is not null)
            {
                var open = live
                    .Where(t => !string.IsNullOrWhiteSpace(t.Id))
                    .Select(t => t.Id)
                    .ToHashSet(StringComparer.Ordinal);

                rows = rows.Where(r => open.Contains(r.FlowableTaskId)).ToList();
            }

            var tasks = rows
                .Select(t => new FlowableTaskSummary
                {
                    Id = t.FlowableTaskId,
                    Name = t.Name ?? string.Empty,
                    TaskDefinitionKey = t.TaskDefinitionKey,
                    Assignee = t.Assignee,
                    ProcessInstanceId = t.FlowableInstanceId,
                    ProcessInstanceName = instance.Name,
                    ProcessDefinitionId = instance.ProcessDefinitionId,

                    // Left null ON PURPOSE. The live path does not set it either
                    // (FlowableClient.GetTasksByProcessInstanceAsync), and filling
                    // it from workflow_model_name would be an improvement -- and an
                    // unrequested content change in a story whose job is to change
                    // where the answer comes from, not what it says.
                    ProcessDefinitionName = null,

                    CreatedAtUtc = new DateTimeOffset(
                        DateTime.SpecifyKind(t.CreatedTime, DateTimeKind.Utc)),
                    DueDate = t.DueDate is { } due
                        ? new DateTimeOffset(DateTime.SpecifyKind(due, DateTimeKind.Utc))
                        : null
                })
                .ToArray();

            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionTasksViewed,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId },
                details: new { resultCount = tasks.Length },
                cancellationToken);
            return Results.Ok(tasks);
        }).RequirePermission(EntityKinds.WorkflowExecution, Actions.View, "processInstanceId");

        // #113. The child instances a call activity in this one started.
        //
        // Without this a stuck call activity is a process with an empty task list
        // and no explanation — the engine knows where the work is and the app had
        // no way to say so.
        executions.MapGet("/{processInstanceId}/children", async (
            string processInstanceId,
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var children = await flowable.GetChildProcessInstancesAsync(
                processInstanceId, cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionChildrenViewed,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId },
                details: new { resultCount = children.Count },
                cancellationToken);
            return Results.Ok(children);
        }).RequirePermission(EntityKinds.WorkflowExecution, Actions.View, "processInstanceId");

        executions.MapGet("/{processInstanceId}/activities/{activityId}/completed-assignees", async (
            string processInstanceId,
            string activityId,
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var assignees = await flowable.GetCompletedAssigneesForActivityAsync(processInstanceId, activityId, cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionCompletedAssigneesViewed,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId, activityId },
                details: new { resultCount = assignees.Count },
                cancellationToken);
            return Results.Ok(assignees);
        }).RequirePermission(EntityKinds.WorkflowExecution, Actions.View, "processInstanceId");

        executions.MapPut("/{processInstanceId}/variables", async (
            string processInstanceId,
            UpdateProcessVariablesRequest request,
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            // #226. `variables` missing from the body deserialises to null, and the
            // next line dereferenced it — a malformed request answered as a 500
            // NullReferenceException. A body we cannot read is the caller's to fix.
            if (request?.Variables is null or { Count: 0 })
            {
                return Results.BadRequest(new
                {
                    message = "The request body must contain a non-empty 'variables' array."
                });
            }

            try
            {
                await flowable.UpdateProcessVariablesAsync(processInstanceId, request.Variables, cancellationToken);
            }
            catch (FlowableRequestException exception) when (exception.IsCallerError)
            {
                // Flowable classified this correctly — 409 for a variable that
                // already exists, 400 for a value its converter cannot take.
                // Re-wrapping it as a 500 loses that and pages someone about a
                // typo. Its 5xx is not caught: that one really is a fault.
                // #350. The raw body carries the engine's whole HTTP response --
                // JDBC URLs, hostnames, absolute paths, Java frames. Three rounds
                // fixed this on the publish route while these four handed it to a
                // wider audience. The raw text survives here and nowhere else.
                loggerFactory.CreateLogger("AutoNate.Web.Executions").LogWarning(
                    exception, "Flowable refused a request. Caller was told: {Described}",
                    EngineRefusal.Describe(exception, "this request"));

                return Results.Json(
                    new { message = EngineRefusal.Describe(exception, "this request") },
                    statusCode: (int)exception.StatusCode);
            }
            // #158: Flowable does not re-evaluate conditional events when a variable
            // changes. Without this, a process parked on `${approved == true}` stays
            // parked after someone sets `approved` to true here — the feature looks
            // broken, and nothing says why. Established by running it against 8.0.0.
            //
            // Unconditional rather than gated on "does this definition have a
            // conditional event": the check would need the diagram, and asking the
            // engine to evaluate an instance with no conditional events is a no-op.
            // #376: the audit record goes FIRST. The variables are already written
            // by this point, and this event is the record of that write -- it must
            // not be contingent on a later step succeeding.
            //
            // The nudge below is deliberately the THROWING overload here (see
            // Outcome 10's note: these two routes differ from the in-client sites
            // on purpose, so a nudge failure surfaces rather than being swallowed).
            // Ordered the other way, a nudge failure meant the variables were set,
            // the caller got a 500, and nothing recorded that the write happened.
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionVariablesSet,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId },
                details: new { variableCount = request.Variables.Count, names = request.Variables.Select(v => v.Name).ToArray() },
                cancellationToken);

            await flowable.EvaluateConditionalEventsAsync(processInstanceId, cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowExecution, Actions.Override, "processInstanceId");

        executions.MapPost("/{processInstanceId}/variables", async (
            string processInstanceId,
            UpdateProcessVariablesRequest request,
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            // #226. `variables` missing from the body deserialises to null, and the
            // next line dereferenced it — a malformed request answered as a 500
            // NullReferenceException. A body we cannot read is the caller's to fix.
            if (request?.Variables is null or { Count: 0 })
            {
                return Results.BadRequest(new
                {
                    message = "The request body must contain a non-empty 'variables' array."
                });
            }

            try
            {
                await flowable.AddProcessVariablesAsync(processInstanceId, request.Variables, cancellationToken);
            }
            catch (FlowableRequestException exception) when (exception.IsCallerError)
            {
                // Flowable classified this correctly — 409 for a variable that
                // already exists, 400 for a value its converter cannot take.
                // Re-wrapping it as a 500 loses that and pages someone about a
                // typo. Its 5xx is not caught: that one really is a fault.
                // #350. The raw body carries the engine's whole HTTP response --
                // JDBC URLs, hostnames, absolute paths, Java frames. Three rounds
                // fixed this on the publish route while these four handed it to a
                // wider audience. The raw text survives here and nowhere else.
                loggerFactory.CreateLogger("AutoNate.Web.Executions").LogWarning(
                    exception, "Flowable refused a request. Caller was told: {Described}",
                    EngineRefusal.Describe(exception, "this request"));

                return Results.Json(
                    new { message = EngineRefusal.Describe(exception, "this request") },
                    statusCode: (int)exception.StatusCode);
            }
            // #158: Flowable does not re-evaluate conditional events when a variable
            // changes. Without this, a process parked on `${approved == true}` stays
            // parked after someone sets `approved` to true here — the feature looks
            // broken, and nothing says why. Established by running it against 8.0.0.
            //
            // Unconditional rather than gated on "does this definition have a
            // conditional event": the check would need the diagram, and asking the
            // engine to evaluate an instance with no conditional events is a no-op.
            // #376: audit first, for the reason given on the sibling route above.
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionVariablesAdded,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId },
                details: new { variableCount = request.Variables.Count, names = request.Variables.Select(v => v.Name).ToArray() },
                cancellationToken);

            await flowable.EvaluateConditionalEventsAsync(processInstanceId, cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowExecution, Actions.Override, "processInstanceId");

        // #163. An ad-hoc subprocess has no predetermined order: the process says
        // what CAN be done and a person decides what happens next. These three
        // endpoints are that person's surface.
        executions.MapGet("/{processInstanceId}/adhoc", async (
            string processInstanceId,
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var states = await flowable.GetAdhocSubProcessesAsync(processInstanceId, cancellationToken);

            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.AdhocActivitiesViewed,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId },
                details: new { subProcessCount = states.Count },
                cancellationToken);

            return Results.Ok(states);
        }).RequirePermission(EntityKinds.WorkflowExecution, Actions.View, "processInstanceId");

        executions.MapPost("/{processInstanceId}/adhoc/{executionId}/activities/{activityId}", async (
            string processInstanceId,
            string executionId,
            string activityId,
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await flowable.StartAdhocActivityAsync(executionId, activityId, cancellationToken);
            }
            catch (FlowableRequestException exception)
            {
                // An activity that is not enabled, or an execution that is not an
                // ad-hoc subprocess. #226's rule: the engine classified it, so do
                // not relabel it as a server fault.
                //
                // #252: the `when (IsCallerError)` this used to carry made the
                // whole block dead code, because the extension answered 500 to
                // every one of those. It classifies them now, and this no longer
                // depends on that -- an engine 5xx still reaches the operator as
                // a defined response carrying the engine's own sentence, rather
                // than escaping as an unhandled exception and rendering as
                // "Could not complete 'adhoc'." with no reason at all.
                // #350. The raw body carries the engine's whole HTTP response --
                // JDBC URLs, hostnames, absolute paths, Java frames. The raw text
                // survives here and nowhere else.
                loggerFactory.CreateLogger("AutoNate.Web.Executions").LogWarning(
                    exception, "Flowable refused a request. Caller was told: {Described}",
                    EngineRefusal.Describe(exception, "this request"));

                return Results.Json(
                    new { message = EngineRefusal.Describe(exception, "this request") },
                    statusCode: exception.IsCallerError
                        ? (int)exception.StatusCode
                        : StatusCodes.Status502BadGateway);
            }

            // An ad-hoc process has no fixed order to reconstruct afterwards, so
            // this record is the only account of what was decided and by whom.
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.AdhocActivityStarted,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId },
                details: new { executionId, activityId },
                cancellationToken);

            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowExecution, Actions.Override, "processInstanceId");

        executions.MapPost("/{processInstanceId}/adhoc/{executionId}/complete", async (
            string processInstanceId,
            string executionId,
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await flowable.CompleteAdhocSubProcessAsync(executionId, cancellationToken);
            }
            catch (FlowableRequestException exception)
            {
                // #252. Finishing a section while an activity inside it is still
                // open is the ordinary operator mistake here, and Flowable refuses
                // it with "Ad-hoc sub process has running child executions that
                // need to be completed first". That arrived as a 500 with no body,
                // which is neither defined nor documented -- the two things #163's
                // AC7 requires of it. The engine now classifies it 409; either
                // way its sentence reaches the operator.
                // #350. The raw body carries the engine's whole HTTP response --
                // JDBC URLs, hostnames, absolute paths, Java frames. The raw text
                // survives here and nowhere else.
                loggerFactory.CreateLogger("AutoNate.Web.Executions").LogWarning(
                    exception, "Flowable refused a request. Caller was told: {Described}",
                    EngineRefusal.Describe(exception, "this request"));

                return Results.Json(
                    new { message = EngineRefusal.Describe(exception, "this request") },
                    statusCode: exception.IsCallerError
                        ? (int)exception.StatusCode
                        : StatusCodes.Status502BadGateway);
            }

            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.AdhocSubProcessCompleted,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId },
                details: new { executionId },
                cancellationToken);

            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowExecution, Actions.Override, "processInstanceId");

        executions.MapPost("/{processInstanceId}/tasks/{taskId}/force-complete", async (
            string processInstanceId,
            string taskId,
            CompleteTaskRequest? request,
            HttpContext http,
            IFlowableClient flowable,
            WorkflowTaskCompletionRecorder completionRecorder,
            WorkflowTaskCacheRefresher cacheRefresher,
            IAuditEventPublisher auditPublisher,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await flowable.CompleteTaskAsync(taskId, request?.Variables, cancellationToken);
            }
            catch (FlowableRequestException exception)
                when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return StaleTask(taskId, exception, loggerFactory);
            }

            // This route names the instance, so the refresher does not have to
            // find it in a cache that may never have seen the task.
            await cacheRefresher.AfterTaskCompletedAsync(
                taskId, cancellationToken, processInstanceId);

            var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(actorId))
            {
                await completionRecorder.RecordAsync(taskId, actorId, wasOverride: true, cancellationToken);
            }
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.TaskForceCompleted,
                WorkflowResourceKinds.Task,
                resource: new { processInstanceId, taskId },
                details: new { hadVariables = request?.Variables is { Count: > 0 } },
                cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowExecution, Actions.Override, "processInstanceId");

        executions.MapPost("/{processInstanceId}/tasks/{taskId}/reassign", async (
            string processInstanceId,
            string taskId,
            ReassignTaskRequest request,
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            await flowable.UpdateTaskAssigneeAsync(taskId, request.Assignee, cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.TaskReassigned,
                WorkflowResourceKinds.Task,
                resource: new { processInstanceId, taskId, assignee = request.Assignee },
                details: null,
                cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowExecution, Actions.Override, "processInstanceId");

        executions.MapPost("/{processInstanceId}/tasks/{taskId}/due-date", async (
            string processInstanceId,
            string taskId,
            UpdateTaskDueDateRequest request,
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            await flowable.UpdateTaskDueDateAsync(taskId, request.DueDate, cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.TaskDueDateChanged,
                WorkflowResourceKinds.Task,
                resource: new { processInstanceId, taskId, dueDate = request.DueDate },
                details: null,
                cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowExecution, Actions.Override, "processInstanceId");

        executions.MapPost("/{processInstanceId}/move-state", async (
            string processInstanceId,
            MoveExecutionStateRequest request,
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.TargetActivityId))
            {
                return Results.BadRequest(new { error = "targetActivityId is required." });
            }

            await flowable.MoveWorkflowExecutionStateAsync(processInstanceId, request.TargetActivityId, cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionStateMoved,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId, targetActivityId = request.TargetActivityId },
                details: null,
                cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowExecution, Actions.MoveState, "processInstanceId");

        executions.MapPost("/{processInstanceId}/cancel", async (
            string processInstanceId,
            IFlowableClient flowable,
            IDbContextFactory<AutoNateDbContext> cacheDbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            await flowable.CancelWorkflowExecutionAsync(processInstanceId, cancellationToken);

            // #609. Marked from the POSITIVE FACT of the cancellation, not read
            // back: a cancelled instance is gone from the engine's RUNTIME, so
            // the read-through that serves the start path would find nothing and
            // delete the row -- and the list is supposed to keep showing it, as
            // cancelled. The poll reconciles the rest from history.
            await using (var cacheDb = await cacheDbFactory.CreateDbContextAsync(cancellationToken))
            {
                await cacheDb.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE workflow_execution_cache
                       SET status = {WorkflowExecutionStatuses.Cancelled},
                           end_time = COALESCE(end_time, {DateTime.UtcNow})
                     WHERE flowable_instance_id = {processInstanceId}
                    """, cancellationToken);
            }

            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionCancelled,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId },
                details: null,
                cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowExecution, Actions.Cancel, "processInstanceId");

        executions.MapDelete("/{processInstanceId}", async (
            string processInstanceId,
            IFlowableClient flowable,
            IDbContextFactory<AutoNateDbContext> cacheDbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            await flowable.DeleteWorkflowExecutionAsync(processInstanceId, cancellationToken);

            // #609. The row goes with it. Unlike cancel, absence IS the intended
            // end state here, and it is a positive fact rather than an inference:
            // this caller just deleted it and the engine agreed.
            await using (var cacheDb = await cacheDbFactory.CreateDbContextAsync(cancellationToken))
            {
                await cacheDb.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM workflow_execution_cache
                     WHERE flowable_instance_id = {processInstanceId}
                    """, cancellationToken);
            }

            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionDeleted,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId },
                details: null,
                cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowExecution, Actions.Delete, "processInstanceId");

        // Bulk wipe — kind-level gate (no instance id). Used by the executions
        // admin page to clear noise during signal-event debugging.
        executions.MapPost("/delete-all", async (
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var deleted = await flowable.DeleteAllWorkflowExecutionsAsync(cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionsBulkDeleted,
                WorkflowResourceKinds.Execution,
                resource: null,
                details: new { deletedCount = deleted },
                cancellationToken);
            return Results.Ok(new { deleted });
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.WorkflowExecution, Actions.DeleteAll);

        var tasks = app.MapGroup("/api/tasks")
            .RequireAuthorization();

        tasks.MapGet("/assigned-to-me", async (
            HttpContext http,
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(actorId))
            {
                return Results.Unauthorized();
            }

            var list = await flowable.GetTasksAssignedToUserAsync(actorId, cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.TasksAssignedToMeViewed,
                WorkflowResourceKinds.Task,
                resource: null,
                details: new { resultCount = list.Count },
                cancellationToken);
            return Results.Ok(list);
        }).AuthorizedInHandler("returns Flowable tasks assigned to the current actor only");

        // Tasks assigned to anyone the actor supervises (entity_edges,
        // edge_kind='supervisor', from = actor). Excludes the actor's own
        // tasks — those go through /assigned-to-me. Surfaced as a separate
        // "Team Tasks" view so supervisors can spot work piling up on their
        // reports without it bleeding into their personal inbox.
        tasks.MapGet("/assigned-to-team", async (
            HttpContext http,
            IFlowableClient flowable,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(actorId))
            {
                return Results.Unauthorized();
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var supervisees = await db.EntityEdges.AsNoTracking()
                .Where(e => e.EdgeKind == EdgeKinds.Supervisor
                         && e.FromKind == EntityKinds.User
                         && e.FromId == actorId
                         && e.ToKind == EntityKinds.User)
                .Select(e => e.ToId)
                .ToListAsync(cancellationToken);

            var list = supervisees.Count == 0
                ? Array.Empty<FlowableTaskSummary>()
                : await flowable.GetTasksAssignedToUsersAsync(supervisees, cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.TasksAssignedToTeamViewed,
                WorkflowResourceKinds.Task,
                resource: null,
                details: new { resultCount = list.Count, superviseeCount = supervisees.Count },
                cancellationToken);
            return Results.Ok(list);
        }).AuthorizedInHandler("returns tasks assigned to the actor's supervisees (entity_edges supervisor walk) only");

        // Drives the SPA's task-action UI: pulls together everything needed to
        // render a task — the BPMN-encoded userForm config (mode + optional
        // form short code), the form snapshot itself, and the current process
        // variables. Lookup chain: task → processDefinitionId →
        // workflow_model_versions row (BPMN xml) → matching <userTask>
        // element → flowable:userForm{Mode,ShortCode} attributes.
        tasks.MapGet("/{taskId}/form-config", async (
            string taskId,
            IFlowableClient flowable,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IFormStore formStore,
            CancellationToken cancellationToken) =>
        {
            var task = await flowable.GetTaskAsync(taskId, cancellationToken);
            if (task is null)
            {
                return Results.NotFound();
            }

            var bpmnXml = await ResolveBpmnXmlForProcessDefinitionAsync(
                dbFactory, task.ProcessDefinitionId, cancellationToken);

            var (mode, formShortCode) = ParseUserFormConfig(bpmnXml, task.TaskDefinitionKey);

            FormWorkflowSnapshot? formSnapshot = null;
            if ((mode == TaskFormModes.Modal || mode == TaskFormModes.Page)
                && !string.IsNullOrWhiteSpace(formShortCode))
            {
                formSnapshot = await formStore
                    .GetWorkflowSnapshotByShortCodeAsync(formShortCode, cancellationToken);
            }

            // Variables only matter when a form is rendered — the simple-
            // complete modal doesn't reference them. Skip the round-trip
            // when we know we don't need them.
            IReadOnlyDictionary<string, JsonElement> variables =
                new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (formSnapshot is not null && !string.IsNullOrWhiteSpace(task.ProcessInstanceId))
            {
                variables = await flowable.GetProcessInstanceVariablesAsync(
                    task.ProcessInstanceId!, cancellationToken);
            }

            // For default-mode tasks: pull <bpmn:documentation> and any
            // exclusive-gateway choices out of the published BPMN so the SPA
            // can render the right modal (single-button vs path-buttons).
            string? description = null;
            IReadOnlyList<GatewayChoiceDto>? gatewayChoices = null;
            if (mode == TaskFormModes.Simple)
            {
                var description_ = WorkflowBpmnXml.TryDescribeGatewayChoices(
                    bpmnXml, task.TaskDefinitionKey);
                if (description_ is not null)
                {
                    description = description_.Description;
                    gatewayChoices = description_.Choices.Count == 0
                        ? null
                        : description_.Choices
                            .Select(choice => new GatewayChoiceDto(
                                choice.FlowId, choice.Label, choice.Description))
                            .ToArray();
                }
            }

            return Results.Ok(new TaskFormConfigDto(
                TaskId: task.Id,
                TaskName: task.Name,
                TaskDefinitionKey: task.TaskDefinitionKey,
                ProcessInstanceId: task.ProcessInstanceId,
                ProcessInstanceName: task.ProcessInstanceName,
                ProcessDefinitionName: task.ProcessDefinitionName,
                Mode: mode,
                FormShortCode: formShortCode,
                Form: formSnapshot,
                Variables: variables,
                Description: description,
                GatewayChoices: gatewayChoices));
        }).RequirePermission(EntityKinds.WorkflowTask, Actions.View, "taskId");

        tasks.MapPost("/{taskId}/complete", async (
            string taskId,
            CompleteTaskRequest? request,
            HttpContext http,
            IFlowableClient flowable,
            WorkflowTaskCompletionRecorder completionRecorder,
            WorkflowTaskCacheRefresher cacheRefresher,
            WorkflowExecutionErrorRecorder errorRecorder,
            IAuditEventPublisher auditPublisher,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await flowable.CompleteTaskAsync(taskId, request?.Variables, cancellationToken);
            }
            catch (FlowableRequestException exception)
                when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return StaleTask(taskId, exception, loggerFactory);
            }
            catch (FlowableRequestException exception)
            {
                return await SynchronousStepFailureAsync(
                    taskId, exception, errorRecorder, cacheRefresher, loggerFactory, cancellationToken);
            }

            // #604. Before the audit event, because this is what makes the
            // caller's next read show their own write.
            await cacheRefresher.AfterTaskCompletedAsync(taskId, cancellationToken);

            var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(actorId))
            {
                await completionRecorder.RecordAsync(taskId, actorId, wasOverride: false, cancellationToken);
            }
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.TaskCompleted,
                WorkflowResourceKinds.Task,
                resource: new { taskId },
                details: new { hadVariables = request?.Variables is { Count: > 0 } },
                cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowTask, Actions.Complete, "taskId");

        // ── #172: jobs and timers ───────────────────────────────────────────
        //
        // Read LIVE from the engine, not from workflow_execution_cache. #104's
        // executions are cached; these deliberately are not, and the difference
        // is stated rather than left to be discovered: the question an operator
        // opens this to answer is "what is stuck right now", and a cache would
        // put a staleness question in front of exactly the reader who cannot
        // tolerate one.

        executions.MapGet("/{processInstanceId}/jobs", async (
            string processInstanceId,
            IFlowableJobClient jobs,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var found = await jobs.GetJobsForExecutionAsync(processInstanceId, cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.JobsViewed,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId },
                details: new { resultCount = found.Count },
                cancellationToken);
            return Results.Ok(found);
        }).RequirePermission(EntityKinds.WorkflowExecution, Actions.View, "processInstanceId");

        executions.MapGet("/{processInstanceId}/jobs/{jobId}/exception", async (
            string processInstanceId,
            string jobId,
            [FromQuery] string? queue,
            IFlowableJobClient jobs,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<WorkflowJobQueue>(queue, ignoreCase: true, out var parsed))
            {
                return Results.BadRequest(new
                {
                    error = $"'{queue}' is not a job queue. Expected one of: "
                        + string.Join(", ", Enum.GetNames<WorkflowJobQueue>())
                });
            }

            // Null is a real answer -- a job can be dead-lettered with a message
            // and no stack -- so it is 200 with null rather than 404, which the
            // caller would have to distinguish from "no such job".
            var stack = await jobs.GetJobExceptionStackAsync(jobId, parsed, cancellationToken);
            return Results.Ok(new { jobId, stack });
        }).RequirePermission(EntityKinds.WorkflowExecution, Actions.View, "processInstanceId");

        executions.MapPost("/{processInstanceId}/jobs/{jobId}/retry", async (
            string processInstanceId,
            string jobId,
            RetryJobRequest request,
            IFlowableJobClient jobs,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<WorkflowJobQueue>(request?.Queue, ignoreCase: true, out var queue))
            {
                return Results.BadRequest(new
                {
                    error = $"'{request?.Queue}' is not a job queue. Expected one of: "
                        + string.Join(", ", Enum.GetNames<WorkflowJobQueue>())
                });
            }

            await jobs.RetryJobAsync(jobId, queue, cancellationToken);

            // Recorded BEFORE the engine has finished acting, and deliberately
            // worded as what was asked for. Retrying a dead-lettered job is a
            // move back to the executable queue; whether the step then succeeds
            // is a separate event on the execution's own stream.
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.JobRetried,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId, jobId },
                details: new { queue = queue.ToString() },
                cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowExecution, Actions.RetryJob, "processInstanceId");

        executions.MapPost("/{processInstanceId}/jobs/{jobId}/reschedule", async (
            string processInstanceId,
            string jobId,
            RescheduleJobRequest request,
            IFlowableJobClient jobs,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            if (request?.DueAtUtc is not { } dueAt)
            {
                return Results.BadRequest(new
                {
                    error = "A reschedule needs a due date. The engine refuses one without it."
                });
            }

            await jobs.RescheduleTimerJobAsync(jobId, dueAt, cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.JobRescheduled,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId, jobId },
                details: new { dueAtUtc = dueAt.ToUniversalTime() },
                cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowExecution, Actions.RescheduleJob, "processInstanceId");

        // The cross-execution stuck list: "what is stuck right now", which is the
        // question an operator opens this page to answer.
        //
        // A FILTER on the executions area rather than its own page (#172,
        // discretion). An operator arrives here from "something is wrong", and a
        // page of its own would need a permanent navigation entry for a list that
        // is empty on a healthy system.
        executions.MapGet("/jobs", async (
            HttpContext http,
            [FromQuery] bool? all,
            IFlowableJobClient jobs,
            IAuthorizer authorizer,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var found = await jobs.GetJobsAsync(deadLetteredOnly: all != true, cancellationToken);

            // Jobs come from the engine; the authorization set comes from the
            // cache. So the two are joined here rather than the list being
            // returned whole -- an operator granted over some executions must not
            // see another team's stuck work just because the engine knows about
            // it.
            //
            // A job whose execution is not in the cache yet is DROPPED. That is
            // the safe direction and it is a real consequence worth naming: a
            // failure in the first seconds of a brand-new instance can be
            // invisible here until the projection catches up. Stated rather than
            // hidden, because the alternative -- showing what cannot be
            // authorized -- is the one that cannot be walked back.
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var authorizedRows = await ExecutionListQuery.BuildAsync(
                db, authorizer, http.User, search: null, status: null, workflowModelId: null,
                cancellationToken);
            var visible = (await authorizedRows.Select(r => r.Id).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var items = found
                .Where(job => job.ProcessInstanceId is { } id && visible.Contains(id))
                .ToArray();

            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.JobsViewed,
                WorkflowResourceKinds.Execution,
                resource: null,
                details: new { resultCount = items.Length, deadLetteredOnly = all != true },
                cancellationToken);

            return Results.Ok(items);
        }).AuthorizedInHandler(
            "filters engine jobs against the ExecutionListQuery authorized set (WorkflowExecution, View)");

        return app;
    }

    public sealed record CompleteTaskRequest(Dictionary<string, object?>? Variables);

    /// <param name="Queue">
    /// Which collection the job is in. Required, and not inferred: each queue
    /// takes a different verb at the engine, and a retry that guessed would be
    /// right two times in three.
    /// </param>
    public sealed record RetryJobRequest(string? Queue);

    public sealed record RescheduleJobRequest(DateTimeOffset? DueAtUtc);

    public sealed record UpdateProcessVariablesRequest(IReadOnlyList<ProcessVariableUpdate> Variables);

    public sealed record ReassignTaskRequest(string? Assignee);

    public sealed record UpdateTaskDueDateRequest(DateTimeOffset? DueDate);

    public sealed record MoveExecutionStateRequest(string TargetActivityId);

    public static class TaskFormModes
    {
        public const string Simple = "simple";
        public const string Modal = "modal";
        public const string Page = "page";
    }

    public sealed record TaskFormConfigDto(
        string TaskId,
        string TaskName,
        string? TaskDefinitionKey,
        string? ProcessInstanceId,
        string? ProcessInstanceName,
        string? ProcessDefinitionName,
        string Mode,
        string? FormShortCode,
        FormWorkflowSnapshot? Form,
        IReadOnlyDictionary<string, JsonElement> Variables,
        string? Description,
        IReadOnlyList<GatewayChoiceDto>? GatewayChoices);

    public sealed record GatewayChoiceDto(string FlowId, string Label, string? Description);


    // internal so the #576 tests can assert the PRODUCTION fact builder supplies
    // `status`. A test that rebuilt the dictionary itself would pass while this
    // method still omitted the tag, which is the defect it exists to catch.
    internal static IReadOnlyDictionary<string, string?> BuildFacts(WorkflowExecutionSummary execution) =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["processkey"] = ExtractProcessKey(execution.ProcessDefinitionId),
            ["definitionkey"] = execution.ProcessDefinitionId,
            ["startedby"] = execution.StartUserId,

            // `status` is advertised and compiles in SQL; until #576 it was
            // never supplied here, so `[status=active]` denied EVERY row on this
            // path while filtering correctly on the other one.
            //
            // Normalized through the shared helper, not passed through raw: the
            // projection writes the normalized string into the status column, so
            // the raw value would make `[status=running]` agree with nothing.
            ["status"] = WorkflowExecutionStatuses.Normalize(execution.Status)
        };

    private static string? ExtractProcessKey(string? processDefinitionId)
    {
        if (string.IsNullOrEmpty(processDefinitionId)) return null;
        var sep = processDefinitionId.IndexOf(':');
        return sep > 0 ? processDefinitionId[..sep] : processDefinitionId;
    }

    /// <summary>
    /// Collapses each multi-instance activity's rows into one carrying progress (#173).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only activities the DIAGRAM marks as multi-instance are collapsed.</b>
    /// Measured on Flowable 8.0.0, history gives a multi-instance three rows with
    /// the same activityId and activityType and no container row — the same shape
    /// a loop or a retried activity produces. Collapsing on repetition would turn
    /// an activity that simply ran twice into "1/2 complete", which is a
    /// regression wearing a feature's clothes.
    /// </para>
    /// <para>
    /// <b>Total prefers the author's literal cardinality over the instance
    /// count</b>, because those differ in exactly the case that matters: a
    /// sequential multi-instance over five items has created ONE instance when
    /// you look at it, and "1/1 complete" for a task that has four runs left is
    /// the wrong answer rather than an imprecise one. A collection-driven loop
    /// declares no literal, and there the instances are all we have.
    /// </para>
    /// <para>
    /// The surviving row keeps the FIRST instance's identity — its start time and
    /// activity name — and drops the per-instance fields (<c>taskId</c>,
    /// <c>assignee</c>) rather than picking one of several to show, which would be
    /// arbitrary. They are available per instance on the expand route.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The name a multi-instance activity binds each collection item to (#173).
    /// </summary>
    /// <remarks>
    /// From the stored diagram, through the one reader that knows the spelling.
    /// Null when the activity is not multi-instance, or is driven by a bare
    /// cardinality -- a loop that counts to three binds no item, so there is
    /// nothing to show and inventing a label would be worse than showing none.
    /// </remarks>
    private static async Task<string?> ResolveElementVariableAsync(
        string processInstanceId,
        string activityId,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IFlowableReadThrough readThrough,
        CancellationToken cancellationToken)
    {
        // #627. THROUGH the read-through, not a bare cache query. The AC says in
        // terms "where the cache cannot answer, it reads through", and a bare
        // FirstOrDefaultAsync returns null on a miss -- so opening a history in
        // the window before the projection lands a row, or after an eviction,
        // silently dropped the element values.
        var processDefinitionId =
            (await readThrough.GetInstanceAsync(processInstanceId, cancellationToken))
            ?.ProcessDefinitionId;

        if (string.IsNullOrWhiteSpace(processDefinitionId)) return null;

        var bpmnXml = await ResolveBpmnXmlForProcessDefinitionAsync(
            dbFactory, processDefinitionId, cancellationToken);

        if (string.IsNullOrWhiteSpace(bpmnXml)) return null;

        return WorkflowBpmnXml.ExtractMultiInstanceActivities(bpmnXml!)
            .TryGetValue(activityId, out var marker) ? marker.ElementVariable : null;
    }

    private static async Task<IReadOnlyList<WorkflowExecutionHistoryEvent>> CollapseMultiInstanceAsync(
        IReadOnlyList<WorkflowExecutionHistoryEvent> rows,
        string processInstanceId,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IFlowableClient flowable,
        IFlowableReadThrough readThrough,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return rows;

        // The definition comes from the execution cache rather than a second
        // engine call: the rows carry no definition id, and #609 populates that
        // row the moment an instance starts. A miss simply means no collapsing --
        // several rows where there should be one is a worse view, not a wrong one.
        // #627. THROUGH the read-through. A bare cache query returned null on a
        // miss and the method then returned `rows` uncollapsed -- so a 50-instance
        // activity rendered as 50 rows with no progress and no error, which is
        // exactly the pre-story behaviour #173 exists to remove, arriving silently.
        var processDefinitionId =
            (await readThrough.GetInstanceAsync(processInstanceId, cancellationToken))
            ?.ProcessDefinitionId;

        if (string.IsNullOrWhiteSpace(processDefinitionId)) return rows;

        var bpmnXml = await ResolveBpmnXmlForProcessDefinitionAsync(
            dbFactory, processDefinitionId, cancellationToken);

        if (string.IsNullOrWhiteSpace(bpmnXml)) return rows;

        var markers = WorkflowBpmnXml.ExtractMultiInstanceActivities(bpmnXml!);
        if (markers.Count == 0) return rows;

        // Asked only now -- a process with no multi-instance marker pays nothing
        // for this, which is most of them.
        MultiInstanceEngineState engine;
        try
        {
            engine = await flowable.GetMultiInstanceEngineStateAsync(processInstanceId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            engine = MultiInstanceEngineState.Empty;
        }

        var collapsed = new List<WorkflowExecutionHistoryEvent>(rows.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!markers.TryGetValue(row.ActivityId, out var marker))
            {
                collapsed.Add(row);
                continue;
            }

            if (!seen.Add(row.ActivityId)) continue;

            var instances = rows
                .Where(e => string.Equals(e.ActivityId, row.ActivityId, StringComparison.Ordinal))
                .ToList();

            var observedCompleted = instances.Count(e => e.EndedAtUtc is not null);
            var observedActive = instances.Count - observedCompleted;

            // The engine's own counters where it still has them, the rows
            // otherwise. MEASURED, and the fallback alone is wrong for exactly one
            // shape: a SEQUENTIAL loop in flight creates one instance at a time, so
            // a loop over five reviewers with one task open writes ONE historic row
            // and counting rows reports "1 of 1". `nrOfInstances` says 5.
            //
            // A finished process has no runtime tree, so nothing comes back and the
            // rows are used -- correct by then, because every instance the loop will
            // ever create has a row.
            engine.CountsByActivityId.TryGetValue(row.ActivityId, out var engineCounts);

            var completed = engineCounts?.Completed ?? observedCompleted;
            var active = engineCounts?.Active ?? observedActive;

            collapsed.Add(row with
            {
                TaskId = null,
                Assignee = null,
                CompletedByUserId = null,
                IsOverride = null,
                EndedAtUtc = active == 0 ? instances.Max(e => e.EndedAtUtc) : null,
                MultiInstance = new MultiInstanceProgress(
                    // The author's declared count first -- it is the only one that
                    // is true before the engine has created anything -- then the
                    // engine's, then the rows.
                    Total: marker.Cardinality ?? engineCounts?.Total ?? instances.Count,
                    Completed: completed,
                    Active: active,

                    // MEASURED, and the first version was wrong. Counting
                    // `IsErrored` over the instances reported THREE failed for one
                    // recorded failure across three instances, because the history
                    // enrichment above stamps that flag on every row sharing the
                    // activity id -- it is an aggregate wearing a per-row name.
                    //
                    // `workflow_execution_errors` is keyed by (process, activity)
                    // and carries no execution or task id, so WHICH instance failed
                    // is not knowable here and no arrangement of this data will make
                    // it so. What is knowable is how many failures were recorded,
                    // which is `ErrorCount` -- clamped to the instances that exist,
                    // because a retried instance would otherwise report more
                    // failures than there are instances to fail.
                    Failed: Math.Min(instances.Max(e => e.ErrorCount) ?? 0, instances.Count),
                    IsSequential: marker.IsSequential)
            });
        }

        return collapsed;
    }

    /// <summary>
    /// A step that failed synchronously, made visible on the execution (#222).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>WorkflowExecutionErrorRecorder</c> listens for one event type,
    /// <c>job.execution.failed</c>, and that is the ONLY thing feeding the red
    /// node on the diagram and <c>isErrored</c> in the history. <b>A step that
    /// fails synchronously produces no job</b>, so it could never arrive there —
    /// the failure took down the API call that caused it and left no trace on the
    /// execution at all.
    /// </para>
    /// <para>
    /// <b>Measured, both halves.</b> A gateway whose condition throws on the
    /// transition out of a completed task leaves the instance alive with three
    /// history rows and none of them errored, and no job to find. The ASYNC path,
    /// by contrast, works and is now guarded — publish force-asyncs script tasks,
    /// DMN tasks, send tasks and the complex-gateway routing script precisely so
    /// their failures become jobs. What is left synchronous is the expressions
    /// evaluated inline during a transition.
    /// </para>
    /// <para>
    /// <b>And the caller gets a sentence rather than whatever the environment
    /// decides.</b> This path had no catch at all. The app installs no exception
    /// middleware, so what an uncaught <c>FlowableRequestException</c> produced
    /// depended entirely on where it ran: in Development the developer exception
    /// page serialised the engine's whole body — problem text, ids, a Java stack —
    /// and in production a bare 500 with an empty body. <b>Stated precisely
    /// because it is tempting to call the first one a production leak and it is
    /// not:</b> the dev page is not on in production. What was wrong everywhere is
    /// that a caller learned nothing actionable and nothing was recorded.
    /// </para>
    /// <para>
    /// <c>NoEndpointReturnsARawEngineMessageTests</c> could not catch this: it
    /// scans for endpoints that <i>put</i> the message into a response, and this
    /// one never mentioned the message at all. A route with no catch is invisible
    /// to a rule about what catches write.
    /// </para>
    /// </remarks>
    private static async Task<IResult> SynchronousStepFailureAsync(
        string taskId,
        FlowableRequestException exception,
        WorkflowExecutionErrorRecorder errorRecorder,
        WorkflowTaskCacheRefresher cacheRefresher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var described = EngineRefusal.Describe(exception, "this step");

        loggerFactory.CreateLogger("AutoNate.Web.WorkflowTasks").LogWarning(
            exception,
            "Completing task {TaskId} failed in the engine. Caller was told: {Described}",
            taskId, described);

        // The instance and the activity come from the cache rather than from the
        // engine's message -- see RecordSynchronousFailureAsync for why parsing
        // that message would be both more precise and spoofable by an author.
        var owner = await cacheRefresher.OwnerOfAsync(taskId, cancellationToken);
        if (owner is { } found)
        {
            // #626. `described`, NOT `exception.Message`.
            //
            // `FlowableRequestException.Message` is built by
            // `FlowableClient.EnsureSuccessAsync` as "Flowable could not {op}.
            // HTTP {code} {reason}. {rawResponseBody}" -- the engine's ENTIRE
            // HTTP body, which `NoEndpointReturnsARawEngineMessageTests`
            // documents as carrying a JDBC URL with its password, internal
            // hostnames, container ids and filesystem paths.
            //
            // This row is served to any caller with WorkflowExecution:View by
            // GET /api/executions/{id}/history. Persisting the raw body here
            // routed around #350 through a different endpoint: the guard scans
            // for `.Message` used INSIDE a FlowableRequestException catch block,
            // and this use is in a helper two hops from the catch.
            //
            // The engine's own words are not lost -- they are in the LogWarning
            // above, where an operator with server access can read them and a
            // caller cannot.
            await errorRecorder.RecordSynchronousFailureAsync(
                found.InstanceId,
                found.ActivityId,
                described,
                exception.StackTrace,
                cancellationToken);
        }

        return Results.Json(
            new
            {
                error = described,
                code = "step_failed",
                taskId
            },
            statusCode: exception.IsCallerError
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status502BadGateway);
    }

    /// <summary>
    /// A task the engine no longer has is a CONFLICT, not a server fault (#604).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Flowable answers <c>404 Could not find a task with id '…'</c> when the id
    /// is one it has already finished. Uncaught, that reached the caller as
    /// <b>500</b>, which tells a UI nothing it can act on — and a stale task id
    /// is an ordinary, expected race for any client that polls a projection:
    /// someone else completed it, or the list was read a moment ago.
    /// </para>
    /// <para>
    /// <b>409, not 404.</b> The route exists and the caller was entitled to it;
    /// what has changed is the state underneath them, and the right thing for a
    /// client to do is re-read the list rather than treat the address as wrong.
    /// </para>
    /// <para>
    /// The engine's own body never reaches the caller (#350) — it carries JDBC
    /// URLs, hostnames and Java frames. It goes to the log; the caller gets a
    /// sentence naming the task and what to do about it.
    /// </para>
    /// </remarks>
    private static IResult StaleTask(
        string taskId, FlowableRequestException exception, ILoggerFactory loggerFactory)
    {
        loggerFactory.CreateLogger("AutoNate.Web.WorkflowTasks").LogWarning(
            exception,
            "Task {TaskId} was already finished when a completion arrived for it.",
            taskId);

        return Results.Json(
            new
            {
                error = $"Task '{taskId}' is no longer open — it has already been completed or "
                    + "cancelled. Refresh the task list and try again.",
                code = "task_not_open",
                taskId
            },
            statusCode: StatusCodes.Status409Conflict);
    }

    private static async Task<string?> ResolveBpmnXmlForProcessDefinitionAsync(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        string? processDefinitionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(processDefinitionId))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.WorkflowModelVersions
            .AsNoTracking()
            .Where(v => v.ProcessDefinitionId == processDefinitionId)
            .OrderByDescending(v => v.PublishedAtUtc)
            .Select(v => v.BpmnXml)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private const string FlowableNamespace = "http://flowable.org/bpmn";

    private static (string Mode, string? FormShortCode) ParseUserFormConfig(
        string? bpmnXml,
        string? taskDefinitionKey)
    {
        if (string.IsNullOrWhiteSpace(bpmnXml) || string.IsNullOrWhiteSpace(taskDefinitionKey))
        {
            return (TaskFormModes.Simple, null);
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(bpmnXml);
        }
        catch (System.Xml.XmlException)
        {
            return (TaskFormModes.Simple, null);
        }

        // The BPMN spec lets several namespace prefixes coexist, so match the
        // userTask element by local name + id rather than a fully-qualified
        // XName. The flowable: attributes are in their own namespace.
        var task = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "userTask"
                                 && (string?)e.Attribute("id") == taskDefinitionKey);
        if (task is null)
        {
            return (TaskFormModes.Simple, null);
        }

        var ns = XNamespace.Get(FlowableNamespace);
        var rawMode = (string?)task.Attribute(ns + "userFormMode");
        var rawShortCode = (string?)task.Attribute(ns + "userFormShortCode");

        var mode = (rawMode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            TaskFormModes.Modal => TaskFormModes.Modal,
            TaskFormModes.Page => TaskFormModes.Page,
            _ => TaskFormModes.Simple
        };

        var shortCode = string.IsNullOrWhiteSpace(rawShortCode) ? null : rawShortCode!.Trim();
        return (mode, shortCode);
    }

    // #218. Swap the deployed diagram for the stored one this instance's version
    // was published from, and map every generated activity id back onto the
    // author's element.
    //
    // Falls back to the deployed XML whenever the stored version cannot be found
    // — an instance older than version tracking, or a definition deployed outside
    // Auton8. Showing the expansion is worse than showing the author's diagram,
    // but far better than showing nothing.
    private static async Task<WorkflowExecutionDiagramDetail> RenderAuthoredDiagramAsync(
        AutoNateDbContext db,
        WorkflowExecutionDiagramDetail detail,
        CancellationToken cancellationToken)
    {
        if (detail.ExpansionSourceIds.Count > 0)
        {
            detail = detail with
            {
                CompletedActivityIds = MapAll(detail, detail.CompletedActivityIds),
                CurrentActivityIds = MapAll(detail, detail.CurrentActivityIds),
                CancelledActivityIds = MapAll(detail, detail.CancelledActivityIds)
            };
        }

        if (string.IsNullOrWhiteSpace(detail.ProcessDefinitionId)) return detail;

        var storedXml = await db.WorkflowModelVersions.AsNoTracking()
            .Where(v => v.ProcessDefinitionId == detail.ProcessDefinitionId)
            .Select(v => v.BpmnXml)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(storedXml) ? detail : detail with { BpmnXml = storedXml };
    }

    private static IReadOnlyList<string> MapAll(
        WorkflowExecutionDiagramDetail detail, IReadOnlyList<string> ids) =>
        ids.Select(id => MapActivityId(detail, id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>A generated activity id becomes the author's element (#218).</summary>
    private static string MapActivityId(WorkflowExecutionDiagramDetail detail, string activityId) =>
        detail.ExpansionSourceIds.TryGetValue(activityId, out var source) ? source : activityId;
}
