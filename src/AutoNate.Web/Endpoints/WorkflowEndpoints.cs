using AutoNate.Web.Models;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Workflow;

namespace AutoNate.Web.Endpoints;

public sealed record PrepareWorkflowRequest(
    WorkflowModel Model,
    IReadOnlyList<WorkflowElementSnapshot> ElementSnapshots);

public sealed record PrepareWorkflowResponse(
    WorkflowModel Model,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workflows")
            .RequireAuthorization();

        group.MapGet("/", async (IWorkflowModelStore store, CancellationToken cancellationToken) =>
        {
            var models = await store.ListAsync(cancellationToken);
            return Results.Ok(models);
        });

        group.MapGet("/latest", async (IWorkflowModelStore store, CancellationToken cancellationToken) =>
        {
            var model = await store.GetMostRecentAsync(cancellationToken);
            return model is null ? Results.NotFound() : Results.Ok(model);
        });

        group.MapGet("/{id:guid}", async (Guid id, IWorkflowModelStore store, CancellationToken cancellationToken) =>
        {
            var model = await store.GetAsync(id, cancellationToken);
            return model is null ? Results.NotFound() : Results.Ok(model);
        });

        group.MapGet("/{id:guid}/versions", async (Guid id, IWorkflowModelStore store, CancellationToken cancellationToken) =>
        {
            var versions = await store.ListVersionsAsync(id, cancellationToken);
            return Results.Ok(versions);
        });

        group.MapPost("/", async (
            WorkflowModel model,
            IWorkflowModelStore store,
            CancellationToken cancellationToken) =>
        {
            var saved = await store.SaveAsync(model, cancellationToken);
            return Results.Ok(saved);
        }).DisableAntiforgery();

        // Normalize the BPMN payload coming from the browser's modeler: patch the process key,
        // workflow name, and element-level snapshots, then validate. The UI calls this before
        // save / publish so the authoritative XML massaging (WorkflowBpmnXml.cs) stays server-side.
        group.MapPost("/prepare", (PrepareWorkflowRequest request) =>
        {
            var workflowName = WorkflowBpmnXml.NormalizeWorkflowName(request.Model.Name);
            var processKey = string.IsNullOrWhiteSpace(request.Model.ProcessKey)
                ? WorkflowBpmnXml.BuildProcessKeyForModel(workflowName)
                : request.Model.ProcessKey;

            string preparedXml;
            try
            {
                preparedXml = WorkflowBpmnXml.ApplyProcessMetadata(
                    request.Model.BpmnXml,
                    processKey,
                    workflowName,
                    request.ElementSnapshots);
            }
            catch (Exception exception)
            {
                return Results.Ok(new PrepareWorkflowResponse(
                    request.Model,
                    Array.Empty<string>(),
                    new[] { exception.Message }));
            }

            var validation = WorkflowBpmnXml.ValidateProcess(preparedXml);
            var prepared = request.Model with
            {
                Name = workflowName,
                ProcessKey = processKey,
                BpmnXml = preparedXml
            };

            return Results.Ok(new PrepareWorkflowResponse(prepared, validation.Warnings, validation.Errors));
        }).DisableAntiforgery();

        group.MapPost("/{id:guid}/publish", async (
            Guid id,
            WorkflowModel model,
            IWorkflowModelStore store,
            IFlowableClient flowable,
            CancellationToken cancellationToken) =>
        {
            if (model.Id != id)
            {
                return Results.BadRequest(new { message = "URL id does not match body Id." });
            }

            var deployment = await flowable.DeployProcessAsync(model, cancellationToken);
            var published = await store.PublishAsync(model, deployment, cancellationToken);
            return Results.Ok(new PublishResponse(published, deployment));
        }).DisableAntiforgery();

        group.MapPost("/{processKey}/start", async (
            string processKey,
            StartInstanceRequest? request,
            IFlowableClient flowable,
            CancellationToken cancellationToken) =>
        {
            var instance = await flowable.StartProcessInstanceAsync(
                processKey,
                request?.Variables,
                cancellationToken);
            return Results.Ok(instance);
        }).DisableAntiforgery();

        return app;
    }

    public sealed record PublishResponse(WorkflowModel Model, WorkflowDeploymentInfo Deployment);

    public sealed record StartInstanceRequest(Dictionary<string, object?>? Variables);
}
