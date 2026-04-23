using AutoNate.Web.Services.Flowable;

namespace AutoNate.Web.Endpoints;

public static class ExecutionEndpoints
{
    public static IEndpointRouteBuilder MapExecutionEndpoints(this IEndpointRouteBuilder app)
    {
        var executions = app.MapGroup("/api/executions")
            .RequireAuthorization();

        executions.MapGet("/", async (IFlowableClient flowable, CancellationToken cancellationToken) =>
        {
            var list = await flowable.GetWorkflowExecutionsAsync(cancellationToken);
            return Results.Ok(list);
        });

        executions.MapGet("/{processInstanceId}/diagram", async (
            string processInstanceId,
            IFlowableClient flowable,
            CancellationToken cancellationToken) =>
        {
            var detail = await flowable.GetWorkflowExecutionDiagramDetailAsync(processInstanceId, cancellationToken);
            return Results.Ok(detail);
        });

        executions.MapGet("/{processInstanceId}/tasks", async (
            string processInstanceId,
            IFlowableClient flowable,
            CancellationToken cancellationToken) =>
        {
            var tasks = await flowable.GetTasksByProcessInstanceAsync(processInstanceId, cancellationToken);
            return Results.Ok(tasks);
        });

        executions.MapDelete("/{processInstanceId}", async (
            string processInstanceId,
            IFlowableClient flowable,
            CancellationToken cancellationToken) =>
        {
            await flowable.DeleteWorkflowExecutionAsync(processInstanceId, cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery();

        var tasks = app.MapGroup("/api/tasks")
            .RequireAuthorization();

        tasks.MapPost("/{taskId}/complete", async (
            string taskId,
            CompleteTaskRequest? request,
            IFlowableClient flowable,
            CancellationToken cancellationToken) =>
        {
            await flowable.CompleteTaskAsync(taskId, request?.Variables, cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery();

        return app;
    }

    public sealed record CompleteTaskRequest(Dictionary<string, object?>? Variables);
}
