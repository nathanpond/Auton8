using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
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

        group.MapGet("/", async (IWorkflowModelStore store, IFlowableClient flowable, CancellationToken cancellationToken) =>
        {
            var models = await store.ListAsync(cancellationToken);
            var suspendedByKey = await BuildSuspendedMapAsync(flowable, cancellationToken);
            var augmented = models.Select(model => WithRuntimeState(model, suspendedByKey)).ToArray();
            return Results.Ok(augmented);
        });

        group.MapGet("/latest", async (IWorkflowModelStore store, IFlowableClient flowable, CancellationToken cancellationToken) =>
        {
            var model = await store.GetMostRecentAsync(cancellationToken);
            if (model is null) return Results.NotFound();
            var augmented = await WithRuntimeStateAsync(flowable, model, cancellationToken);
            return Results.Ok(augmented);
        });

        group.MapGet("/{id:guid}", async (Guid id, IWorkflowModelStore store, IFlowableClient flowable, CancellationToken cancellationToken) =>
        {
            var model = await store.GetAsync(id, cancellationToken);
            if (model is null) return Results.NotFound();
            var augmented = await WithRuntimeStateAsync(flowable, model, cancellationToken);
            return Results.Ok(augmented);
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
            // A fresh deployment is always active in Flowable — null out any
            // stale suspended flag so the SPA shows "Pause" rather than "Resume".
            var augmented = published with { IsSuspended = false };
            return Results.Ok(new PublishResponse(augmented, deployment));
        }).DisableAntiforgery();

        group.MapPost("/{processKey}/start", async (
            string processKey,
            StartInstanceRequest? request,
            IFlowableClient flowable,
            IWorkflowModelStore store,
            CancellationToken cancellationToken) =>
        {
            // Caller can pass an explicit Name (richer call sites will). When
            // they don't (Studio "Start Instance" button) we generate
            // "ModelName (N)" using Flowable's running total of executions
            // for this definition. Best-effort: if the model lookup or count
            // fails, fall back to letting Flowable assign no name.
            var name = request?.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                name = await TryGenerateInstanceNameAsync(flowable, store, processKey, cancellationToken);
            }

            var instance = await flowable.StartProcessInstanceAsync(
                processKey,
                name,
                request?.Variables,
                cancellationToken);
            return Results.Ok(instance);
        }).DisableAntiforgery();

        group.MapPost("/{id:guid}/pause", async (
            Guid id,
            IWorkflowModelStore store,
            IFlowableClient flowable,
            CancellationToken cancellationToken) =>
        {
            var model = await store.GetAsync(id, cancellationToken);
            if (model is null) return Results.NotFound();
            if (model.LastDeployment is null || string.IsNullOrWhiteSpace(model.LastDeployment.ProcessDefinitionKey))
            {
                return Results.BadRequest(new { message = "This workflow has not been published to Flowable yet, so it cannot be paused." });
            }

            await flowable.SuspendProcessDefinitionAsync(model.LastDeployment.ProcessDefinitionKey, cancellationToken);
            var augmented = await WithRuntimeStateAsync(flowable, model, cancellationToken);
            return Results.Ok(augmented);
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowModel, Actions.Pause, "id");

        group.MapPost("/{id:guid}/resume", async (
            Guid id,
            IWorkflowModelStore store,
            IFlowableClient flowable,
            CancellationToken cancellationToken) =>
        {
            var model = await store.GetAsync(id, cancellationToken);
            if (model is null) return Results.NotFound();
            if (model.LastDeployment is null || string.IsNullOrWhiteSpace(model.LastDeployment.ProcessDefinitionKey))
            {
                return Results.BadRequest(new { message = "This workflow has not been published to Flowable yet, so it cannot be resumed." });
            }

            await flowable.ActivateProcessDefinitionAsync(model.LastDeployment.ProcessDefinitionKey, cancellationToken);
            var augmented = await WithRuntimeStateAsync(flowable, model, cancellationToken);
            return Results.Ok(augmented);
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowModel, Actions.Pause, "id");

        return app;
    }

    private static async Task<Dictionary<string, bool>> BuildSuspendedMapAsync(
        IFlowableClient flowable,
        CancellationToken cancellationToken)
    {
        // Tolerate Flowable being unreachable on the list endpoint — the
        // workflow page must still render the studio when Flowable is down.
        // The pause/resume actions themselves still bubble Flowable errors.
        try
        {
            var definitions = await flowable.GetLatestProcessDefinitionsAsync(cancellationToken);
            return definitions
                .Where(definition => !string.IsNullOrWhiteSpace(definition.Key))
                .GroupBy(definition => definition.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Suspended, StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, bool>(StringComparer.Ordinal);
        }
    }

    private static async Task<WorkflowModel> WithRuntimeStateAsync(
        IFlowableClient flowable,
        WorkflowModel model,
        CancellationToken cancellationToken)
    {
        if (model.LastDeployment is null || string.IsNullOrWhiteSpace(model.LastDeployment.ProcessDefinitionKey))
        {
            return model with { IsSuspended = null };
        }

        try
        {
            var definition = await flowable.GetLatestProcessDefinitionAsync(model.LastDeployment.ProcessDefinitionKey, cancellationToken);
            return model with { IsSuspended = definition?.Suspended };
        }
        catch
        {
            return model with { IsSuspended = null };
        }
    }

    private static WorkflowModel WithRuntimeState(WorkflowModel model, IReadOnlyDictionary<string, bool> suspendedByKey)
    {
        if (model.LastDeployment is null || string.IsNullOrWhiteSpace(model.LastDeployment.ProcessDefinitionKey))
        {
            return model with { IsSuspended = null };
        }

        return suspendedByKey.TryGetValue(model.LastDeployment.ProcessDefinitionKey, out var suspended)
            ? model with { IsSuspended = suspended }
            : model with { IsSuspended = null };
    }

    private static async Task<string?> TryGenerateInstanceNameAsync(
        IFlowableClient flowable,
        IWorkflowModelStore store,
        string processKey,
        CancellationToken cancellationToken)
    {
        var model = await store.GetByProcessKeyAsync(processKey, cancellationToken);
        var label = string.IsNullOrWhiteSpace(model?.Name) ? processKey : model!.Name;

        var existing = await flowable.GetHistoricProcessInstanceCountByDefinitionKeyAsync(processKey, cancellationToken);
        return $"{label} ({existing + 1})";
    }

    public sealed record PublishResponse(WorkflowModel Model, WorkflowDeploymentInfo Deployment);

    public sealed record StartInstanceRequest(string? Name, Dictionary<string, object?>? Variables);
}
