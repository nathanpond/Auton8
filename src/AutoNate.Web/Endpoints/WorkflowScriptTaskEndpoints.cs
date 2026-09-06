using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Pipelines.Execution;
using AutoNate.Web.Services.Workflow;

namespace AutoNate.Web.Endpoints;

// What the Flowable extension POSTs when a BPMN script task executes (#147).
public sealed record WorkflowScriptTaskRequest(
    string ProcessInstanceId,
    string ExecutionId,
    string NodeId,
    string Code,
    IReadOnlyDictionary<string, object?>? Variables,
    string? CorrelationId);

// The mutations the engine applies to the execution, and the value backing
// `resultVariable`.
public sealed record WorkflowScriptTaskResponse(
    object? Result,
    IReadOnlyDictionary<string, object?> Mutations);

public static class WorkflowScriptTaskEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowScriptTaskEndpoints(this IEndpointRouteBuilder app)
    {
        // Anonymous + secret-gated for the same reason as
        // /api/workflow-behaviors/{key}/execute: the caller is the Flowable
        // JVM, which has no cookie to ride on. Same filter, same header.
        var group = app.MapGroup("/api/workflow-script-tasks").AllowAnonymous();

        group.MapPost("/execute", async (
            WorkflowScriptTaskRequest request,
            IScriptTaskRunner runner,
            ILogger<WorkflowScriptTaskResponse> log,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                // An empty script is an authoring error, not a runtime one, and
                // #151 rejects it at publish time. Reaching here means it got
                // past that, so it fails deterministically rather than quietly
                // succeeding as a no-op.
                return Results.UnprocessableEntity(new
                {
                    error = "script_error",
                    message = "Script task has no script body.",
                });
            }

            try
            {
                var result = await runner.RunScriptTaskAsync(
                    request.ProcessInstanceId,
                    request.NodeId,
                    request.Code,
                    request.Variables ?? new Dictionary<string, object?>(StringComparer.Ordinal),
                    cancellationToken);

                return Results.Ok(new WorkflowScriptTaskResponse(result.Result, result.Mutations));
            }
            catch (ScriptExecutionException e)
            {
                // The author's code failed. Deterministic: retrying runs the
                // same broken script again. 422 tells the extension to fail the
                // activity without presenting it as an infrastructure fault.
                log.LogInformation(
                    "Script task {NodeId} in process {ProcessInstanceId} failed: {Message}",
                    request.NodeId, request.ProcessInstanceId, e.Message);
                return Results.UnprocessableEntity(new { error = "script_error", message = e.Message });
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The sandbox could not be reached. Retryable, and explicitly
                // NOT a fallback: nothing here runs the script by another
                // route. A fallback would reintroduce GHSA-82rh-gjhw-rg9r
                // precisely when the system is degraded.
                log.LogError(e,
                    "Script task {NodeId} in process {ProcessInstanceId} could not reach the executor.",
                    request.NodeId, request.ProcessInstanceId);
                return Results.Json(
                    new { error = "executor_unavailable", message = e.Message },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .DisableAntiforgery()
        .AddEndpointFilter<SharedSecretEndpointFilter>();

        MapTestRun(app);
        return app;
    }

    // #152: an author runs a script against sample variables from the editor,
    // before publishing.
    //
    // The value of this panel depends entirely on it being the SAME sandbox
    // production uses — a test environment that is more permissive teaches
    // authors the wrong thing and is worse than none. So it calls the same
    // IScriptTaskRunner the Flowable callback calls, rather than constructing
    // an evaluator of its own. There is nothing here to drift.
    //
    // Nothing is persisted and no process instance exists: the runner is given
    // a synthetic scope, and the mutations come back to the caller instead of
    // being applied to an execution.
    private static void MapTestRun(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/workflow-script-tasks/test-run", async (
            WorkflowScriptTestRunRequest request,
            IScriptTaskRunner runner,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return Results.Ok(WorkflowScriptTestRunResponse.Failure(
                    "script_error", "There is no script to run."));
            }

            var inputs = request.Variables ?? new Dictionary<string, object?>(StringComparer.Ordinal);
            try
            {
                var result = await runner.RunScriptTaskAsync(
                    processInstanceId: "test-run",
                    nodeId: "test-run",
                    code: request.Code,
                    variables: inputs,
                    cancellationToken);

                // The author should see what the script *changed*, not be left
                // to diff a full dump against what they typed in.
                var changed = result.Mutations.Keys
                    .Where(name => !inputs.TryGetValue(name, out var before)
                        || !JsonEquals(before, result.Mutations[name]))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                return Results.Ok(new WorkflowScriptTestRunResponse(
                    Ok: true,
                    Result: result.Result,
                    Mutations: result.Mutations,
                    Changed: changed,
                    ErrorKind: null,
                    ErrorMessage: null));
            }
            catch (ScriptExecutionException e)
            {
                // A refusal is not a bug, and this panel is where an author
                // learns the boundary. Classified from the same list that
                // drives publish-time rejection, so the two cannot disagree.
                var refusal = ScriptSurfaceRules.TryExplainRefusal(e.Message);
                return Results.Ok(refusal is null
                    ? WorkflowScriptTestRunResponse.Failure("script_error", e.Message)
                    : WorkflowScriptTestRunResponse.Failure("sandbox_refusal", refusal));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return Results.Ok(WorkflowScriptTestRunResponse.Failure(
                    "executor_unavailable",
                    "The script sandbox could not be reached, so the script was not run."));
            }
        })
        // Running a test executes author-supplied code, so it is not a lesser
        // operation than authoring it — same gate as editing the workflow.
        .RequireKindPermission(EntityKinds.WorkflowModel, Actions.Edit);
    }

    // Whether a variable actually changed.
    //
    // Compared as serialized JSON rather than with Equals: the inputs arrive
    // off the wire as JsonElement while the mutations come back as whatever the
    // sandbox produced, so reference and value equality both report every
    // variable as changed — which would defeat the point of showing the author
    // only what their script touched.
    private static bool JsonEquals(object? left, object? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return string.Equals(
            JsonSerializer.Serialize(left, SerializerOptions),
            JsonSerializer.Serialize(right, SerializerOptions),
            StringComparison.Ordinal);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
}

// #152 test-run contract.
public sealed record WorkflowScriptTestRunRequest(
    string Code,
    IReadOnlyDictionary<string, object?>? Variables);

public sealed record WorkflowScriptTestRunResponse(
    bool Ok,
    object? Result,
    IReadOnlyDictionary<string, object?>? Mutations,
    IReadOnlyList<string>? Changed,
    // "script_error" | "sandbox_refusal" | "executor_unavailable". Kept
    // distinct because a refusal is the sandbox working, not a bug, and an
    // author who cannot tell them apart learns the wrong lesson from each.
    string? ErrorKind,
    string? ErrorMessage)
{
    public static WorkflowScriptTestRunResponse Failure(string kind, string message) =>
        new(false, null, null, null, kind, message);
}
