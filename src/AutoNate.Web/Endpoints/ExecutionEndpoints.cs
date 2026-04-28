using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Flowable;
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
            CancellationToken cancellationToken) =>
        {
            var list = await flowable.GetWorkflowExecutionsAsync(cancellationToken);
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
            CancellationToken cancellationToken) =>
        {
            var detail = await flowable.GetWorkflowExecutionDiagramDetailAsync(processInstanceId, cancellationToken);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var failedActivityIds = await db.WorkflowExecutionErrors.AsNoTracking()
                .Where(e => e.ProcessInstanceId == processInstanceId)
                .Select(e => e.ActivityId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (failedActivityIds.Count == 0)
            {
                return Results.Ok(detail);
            }

            return Results.Ok(detail with { FailedActivityIds = failedActivityIds });
        }).RequirePermission(EntityKinds.WorkflowExecution, Actions.View, "processInstanceId");

        executions.MapGet("/{processInstanceId}/tasks", async (
            string processInstanceId,
            IFlowableClient flowable,
            CancellationToken cancellationToken) =>
        {
            var tasks = await flowable.GetTasksByProcessInstanceAsync(processInstanceId, cancellationToken);
            return Results.Ok(tasks);
        }).RequirePermission(EntityKinds.WorkflowExecution, Actions.View, "processInstanceId");

        executions.MapGet("/{processInstanceId}/activities/{activityId}/completed-assignees", async (
            string processInstanceId,
            string activityId,
            IFlowableClient flowable,
            CancellationToken cancellationToken) =>
        {
            var assignees = await flowable.GetCompletedAssigneesForActivityAsync(processInstanceId, activityId, cancellationToken);
            return Results.Ok(assignees);
        }).RequirePermission(EntityKinds.WorkflowExecution, Actions.View, "processInstanceId");

        executions.MapPut("/{processInstanceId}/variables", async (
            string processInstanceId,
            UpdateProcessVariablesRequest request,
            IFlowableClient flowable,
            CancellationToken cancellationToken) =>
        {
            await flowable.UpdateProcessVariablesAsync(processInstanceId, request.Variables, cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowExecution, Actions.Override, "processInstanceId");

        executions.MapPost("/{processInstanceId}/tasks/{taskId}/force-complete", async (
            string processInstanceId,
            string taskId,
            CompleteTaskRequest? request,
            IFlowableClient flowable,
            CancellationToken cancellationToken) =>
        {
            await flowable.CompleteTaskAsync(taskId, request?.Variables, cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowExecution, Actions.Override, "processInstanceId");

        executions.MapPost("/{processInstanceId}/cancel", async (
            string processInstanceId,
            IFlowableClient flowable,
            CancellationToken cancellationToken) =>
        {
            await flowable.CancelWorkflowExecutionAsync(processInstanceId, cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowExecution, Actions.Cancel, "processInstanceId");

        executions.MapDelete("/{processInstanceId}", async (
            string processInstanceId,
            IFlowableClient flowable,
            CancellationToken cancellationToken) =>
        {
            await flowable.DeleteWorkflowExecutionAsync(processInstanceId, cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowExecution, Actions.Delete, "processInstanceId");

        // Bulk wipe — kind-level gate (no instance id). Used by the executions
        // admin page to clear noise during signal-event debugging.
        executions.MapPost("/delete-all", async (
            IFlowableClient flowable,
            CancellationToken cancellationToken) =>
        {
            var deleted = await flowable.DeleteAllWorkflowExecutionsAsync(cancellationToken);
            return Results.Ok(new { deleted });
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.WorkflowExecution, Actions.DeleteAll);

        var tasks = app.MapGroup("/api/tasks")
            .RequireAuthorization();

        tasks.MapGet("/assigned-to-me", async (
            HttpContext http,
            IFlowableClient flowable,
            CancellationToken cancellationToken) =>
        {
            var actorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(actorId))
            {
                return Results.Unauthorized();
            }

            var list = await flowable.GetTasksAssignedToUserAsync(actorId, cancellationToken);
            return Results.Ok(list);
        });

        // Tasks the actor can see — their own plus tasks of any user they
        // supervise (entity_edges, edge_kind='supervisor', from = actor).
        // The Complete button on each row is gated separately via
        // POST /api/auth/check, so the same list serves "I can act" and
        // "I can only watch" cases.
        tasks.MapGet("/visible-to-me", async (
            HttpContext http,
            IFlowableClient flowable,
            IDbContextFactory<AutoNateDbContext> dbFactory,
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

            var users = new List<string>(supervisees.Count + 1) { actorId };
            users.AddRange(supervisees);

            var list = await flowable.GetTasksAssignedToUsersAsync(users, cancellationToken);
            return Results.Ok(list);
        });

        tasks.MapPost("/{taskId}/complete", async (
            string taskId,
            CompleteTaskRequest? request,
            IFlowableClient flowable,
            CancellationToken cancellationToken) =>
        {
            await flowable.CompleteTaskAsync(taskId, request?.Variables, cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowTask, Actions.Complete, "taskId");

        return app;
    }

    public sealed record CompleteTaskRequest(Dictionary<string, object?>? Variables);

    public sealed record UpdateProcessVariablesRequest(IReadOnlyList<ProcessVariableUpdate> Variables);
}
