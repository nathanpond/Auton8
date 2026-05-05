using System.Security.Claims;
using System.Text.Json;
using System.Xml.Linq;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Models;
using AutoNate.Web.Models.Forms;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Flowable;
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

        executions.MapGet("/", async (
            IFlowableClient flowable,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var list = await flowable.GetWorkflowExecutionsAsync(cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionListViewed,
                WorkflowResourceKinds.Execution,
                resource: null,
                details: new { resultCount = list.Count },
                cancellationToken);
            if (list.Count == 0)
            {
                return Results.Ok(list);
            }

            // List<string> (not string[]) so EF Core's expression interpreter
            // binds to List<T>.Contains instead of the newer ReadOnlySpan<T>
            // Contains overload, which the funcletizer can't translate.
            var ids = list.Select(execution => execution.Id).ToList();

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var erroredInstanceIds = await db.WorkflowExecutionErrors.AsNoTracking()
                .Where(e => ids.Contains(e.ProcessInstanceId))
                .Select(e => e.ProcessInstanceId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (erroredInstanceIds.Count == 0)
            {
                return Results.Ok(list);
            }

            // "Errored" wins over Running/Complete in the UI: a process with any
            // failed job is still actionable but no longer healthy. Cancelled
            // wins over Errored — operator intent supersedes a stale failure.
            var erroredSet = new HashSet<string>(erroredInstanceIds, StringComparer.Ordinal);
            var projected = list
                .Select(execution => execution.Status == "Cancelled" || !erroredSet.Contains(execution.Id)
                    ? execution
                    : execution with { Status = "Errored" })
                .ToArray();
            return Results.Ok(projected);
        });

        executions.MapGet("/{processInstanceId}/diagram", async (
            string processInstanceId,
            IFlowableClient flowable,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var detail = await flowable.GetWorkflowExecutionDiagramDetailAsync(processInstanceId, cancellationToken);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var errorRows = await db.WorkflowExecutionErrors.AsNoTracking()
                .Where(e => e.ProcessInstanceId == processInstanceId)
                .OrderBy(e => e.OccurredAtUtc)
                .ToListAsync(cancellationToken);

            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionDiagramViewed,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId },
                details: new { failedActivityCount = errorRows.Select(e => e.ActivityId).Distinct().Count() },
                cancellationToken);

            if (errorRows.Count == 0)
            {
                return Results.Ok(detail);
            }

            var failedActivityIds = errorRows
                .Select(e => e.ActivityId)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // Latest non-empty message per activity. We take the freshest error
            // because retries can produce successively different messages and the
            // most recent one is what the operator wants to see in the tooltip.
            var errorMessagesByActivityId = errorRows
                .GroupBy(e => e.ActivityId, StringComparer.Ordinal)
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
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var history = await flowable.GetWorkflowExecutionHistoryAsync(processInstanceId, cancellationToken);
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
            var errorRows = await db.WorkflowExecutionErrors.AsNoTracking()
                .Where(e => e.ProcessInstanceId == processInstanceId)
                .OrderBy(e => e.OccurredAtUtc)
                .ToListAsync(cancellationToken);

            var errorsByActivity = errorRows
                .GroupBy(e => e.ActivityId, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        Count = g.Count(),
                        // Latest non-empty message wins. Today these are
                        // mostly null until the Java-side capture lands.
                        LatestMessage = g.Reverse()
                            .Select(e => e.ErrorMessage)
                            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
                    },
                    StringComparer.Ordinal);

            if (completions.Count == 0 && errorsByActivity.Count == 0)
            {
                return Results.Ok(history);
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
                            ErrorMessage = errorAgg.LatestMessage
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

            foreach (var errorRow in errorRows.GroupBy(e => e.ActivityId, StringComparer.Ordinal))
            {
                if (historyActivityIds.Contains(errorRow.Key))
                {
                    continue;
                }

                var first = errorRow.OrderBy(e => e.OccurredAtUtc).First();
                var latestMessage = errorRow.Reverse()
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
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
                    ErrorMessage = latestMessage
                });
            }

            var sorted = enriched
                .OrderBy(e => e.StartedAtUtc ?? DateTimeOffset.MinValue)
                .ToArray();

            return Results.Ok(sorted);
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

        executions.MapGet("/{processInstanceId}/tasks", async (
            string processInstanceId,
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var tasks = await flowable.GetTasksByProcessInstanceAsync(processInstanceId, cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionTasksViewed,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId },
                details: new { resultCount = tasks.Count },
                cancellationToken);
            return Results.Ok(tasks);
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
            CancellationToken cancellationToken) =>
        {
            await flowable.UpdateProcessVariablesAsync(processInstanceId, request.Variables, cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionVariablesSet,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId },
                details: new { variableCount = request.Variables.Count, names = request.Variables.Select(v => v.Name).ToArray() },
                cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowExecution, Actions.Override, "processInstanceId");

        executions.MapPost("/{processInstanceId}/variables", async (
            string processInstanceId,
            UpdateProcessVariablesRequest request,
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            await flowable.AddProcessVariablesAsync(processInstanceId, request.Variables, cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ExecutionVariablesAdded,
                WorkflowResourceKinds.Execution,
                resource: new { processInstanceId },
                details: new { variableCount = request.Variables.Count, names = request.Variables.Select(v => v.Name).ToArray() },
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
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            await flowable.CompleteTaskAsync(taskId, request?.Variables, cancellationToken);

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
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            await flowable.CancelWorkflowExecutionAsync(processInstanceId, cancellationToken);
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
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            await flowable.DeleteWorkflowExecutionAsync(processInstanceId, cancellationToken);
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
        });

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
        });

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
                Variables: variables));
        }).RequirePermission(EntityKinds.WorkflowTask, Actions.View, "taskId");

        tasks.MapPost("/{taskId}/complete", async (
            string taskId,
            CompleteTaskRequest? request,
            HttpContext http,
            IFlowableClient flowable,
            WorkflowTaskCompletionRecorder completionRecorder,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            await flowable.CompleteTaskAsync(taskId, request?.Variables, cancellationToken);

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

        return app;
    }

    public sealed record CompleteTaskRequest(Dictionary<string, object?>? Variables);

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
        IReadOnlyDictionary<string, JsonElement> Variables);

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
        // XName. The flowable: attributes are in their own namespace; look up
        // both the namespaced form (what bpmn-js writes) and the legacy
        // attribute-only form just in case.
        var task = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "userTask"
                                 && (string?)e.Attribute("id") == taskDefinitionKey);
        if (task is null)
        {
            return (TaskFormModes.Simple, null);
        }

        var ns = XNamespace.Get(FlowableNamespace);
        var rawMode = (string?)task.Attribute(ns + "userFormMode")
                      ?? (string?)task.Attribute("flowable:userFormMode");
        var rawShortCode = (string?)task.Attribute(ns + "userFormShortCode")
                           ?? (string?)task.Attribute("flowable:userFormShortCode");

        var mode = (rawMode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            TaskFormModes.Modal => TaskFormModes.Modal,
            TaskFormModes.Page => TaskFormModes.Page,
            _ => TaskFormModes.Simple
        };

        var shortCode = string.IsNullOrWhiteSpace(rawShortCode) ? null : rawShortCode!.Trim();
        return (mode, shortCode);
    }
}
