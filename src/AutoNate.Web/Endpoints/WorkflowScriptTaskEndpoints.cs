using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Pipelines.Execution;

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

        return app;
    }
}
