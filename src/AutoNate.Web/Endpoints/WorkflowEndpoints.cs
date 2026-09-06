using System.Security.Claims;
using System.Text.Json;
using System.Xml.Linq;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Workflow;
using Microsoft.EntityFrameworkCore;

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

        group.MapGet("/", async (
            IWorkflowModelStore store, IFlowableClient flowable,
            IAuditEventPublisher auditPublisher, CancellationToken cancellationToken) =>
        {
            var models = await store.ListAsync(cancellationToken);
            var suspendedByKey = await BuildSuspendedMapAsync(flowable, cancellationToken);
            var augmented = models.Select(model => WithRuntimeState(model, suspendedByKey)).ToArray();
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ModelListViewed,
                WorkflowResourceKinds.WorkflowModel,
                resource: null,
                details: new { resultCount = augmented.Length },
                cancellationToken);
            return Results.Ok(augmented);
        }).RequireKindPermission(EntityKinds.WorkflowModel, Actions.View);

        group.MapGet("/latest", async (
            IWorkflowModelStore store, IFlowableClient flowable,
            IAuditEventPublisher auditPublisher, CancellationToken cancellationToken) =>
        {
            var model = await store.GetMostRecentAsync(cancellationToken);
            if (model is null) return Results.NotFound();
            var augmented = await WithRuntimeStateAsync(flowable, model, cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ModelLatestViewed,
                WorkflowResourceKinds.WorkflowModel,
                resource: new { id = augmented.Id, name = augmented.Name, processKey = augmented.ProcessKey },
                details: null,
                cancellationToken);
            return Results.Ok(augmented);
        }).RequireKindPermission(EntityKinds.WorkflowModel, Actions.View);

        group.MapGet("/{id:guid}", async (
            Guid id, IWorkflowModelStore store, IFlowableClient flowable,
            IAuditEventPublisher auditPublisher, CancellationToken cancellationToken) =>
        {
            var model = await store.GetAsync(id, cancellationToken);
            if (model is null) return Results.NotFound();
            var augmented = await WithRuntimeStateAsync(flowable, model, cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ModelViewed,
                WorkflowResourceKinds.WorkflowModel,
                resource: new { id = augmented.Id, name = augmented.Name, processKey = augmented.ProcessKey },
                details: null,
                cancellationToken);
            return Results.Ok(augmented);
        }).RequirePermission(EntityKinds.WorkflowModel, Actions.View, "id");

        group.MapGet("/{id:guid}/versions", async (
            Guid id, IWorkflowModelStore store,
            IAuditEventPublisher auditPublisher, CancellationToken cancellationToken) =>
        {
            var versions = await store.ListVersionsAsync(id, cancellationToken);
            // Snapshot the model so the audit log shows the name instead of
            // a bare UUID; no extra Flowable round-trip needed for versions.
            var snapshot = await store.GetAsync(id, cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ModelVersionsViewed,
                WorkflowResourceKinds.WorkflowModel,
                resource: new { id, name = snapshot?.Name, processKey = snapshot?.ProcessKey },
                details: new { resultCount = versions.Count },
                cancellationToken);
            return Results.Ok(versions);
        }).RequirePermission(EntityKinds.WorkflowModel, Actions.View, "id");

        // Telemetry-only endpoint: publishes a ModelViewed event WITHOUT
        // re-fetching the model BPMN or talking to Flowable. The SPA's
        // Workflow Studio loads all models in one list call, then switches
        // between them locally; this gives the studio a way to record each
        // switch as a discrete view event so an audit consumer (the Auditor
        // plugin) sees one row per model the user inspected.
        group.MapPost("/{id:guid}/viewed", async (
            Guid id, IWorkflowModelStore store,
            IAuditEventPublisher auditPublisher, CancellationToken cancellationToken) =>
        {
            var snapshot = await store.GetAsync(id, cancellationToken);
            if (snapshot is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ModelViewed,
                WorkflowResourceKinds.WorkflowModel,
                resource: new { id = snapshot.Id, name = snapshot.Name, processKey = snapshot.ProcessKey },
                details: new { source = "studio" },
                cancellationToken);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowModel, Actions.View, "id");

        // Save covers both initial create and subsequent edits (the id is in
        // the body, not the route), so gate at the kind level. Per-instance
        // restrictions on which model an admin may edit are enforced by their
        // grants on the (workflowmodel, edit, /workflowmodel/{id}) selector.
        group.MapPost("/", async (
            WorkflowModel model,
            IWorkflowModelStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var saved = await store.SaveAsync(model, cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ModelSaved,
                WorkflowResourceKinds.WorkflowModel,
                resource: new { id = saved.Id, name = saved.Name, processKey = saved.ProcessKey },
                details: null,
                cancellationToken);
            return Results.Ok(saved);
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.WorkflowModel, Actions.Edit);

        // Normalize the BPMN payload coming from the browser's modeler: patch the process key,
        // workflow name, and element-level snapshots, then validate. The UI calls this before
        // save / publish so the authoritative XML massaging (WorkflowBpmnXml.cs) stays server-side.
        group.MapPost("/prepare", async (
            PrepareWorkflowRequest request,
            IDbContextFactory<AutoNateDbContext> dbContextFactory,
            CancellationToken cancellationToken) =>
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
            // Warning-only DB-aware rule: surface signal-filter shortcodes that
            // don't exist in this environment yet. Publish proceeds either way.
            var dbWarnings = await WorkflowBpmnXml.BuildRecordTypeShortCodeWarningsAsync(
                preparedXml, dbContextFactory, cancellationToken);

            var combinedWarnings = dbWarnings.Count == 0
                ? validation.Warnings
                : validation.Warnings.Concat(dbWarnings).ToArray();

            var prepared = request.Model with
            {
                Name = workflowName,
                ProcessKey = processKey,
                BpmnXml = preparedXml
            };

            return Results.Ok(new PrepareWorkflowResponse(prepared, combinedWarnings, validation.Errors));
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.WorkflowModel, Actions.Edit);

        group.MapPost("/{id:guid}/publish", async (
            Guid id,
            WorkflowModel model,
            IWorkflowModelStore store,
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
            IAuthorizer authorizer,
            ClaimsPrincipal actor,
            CancellationToken cancellationToken) =>
        {
            if (model.Id != id)
            {
                return Results.BadRequest(new { message = "URL id does not match body Id." });
            }

            // #153: a script task declaring `runAs="system"` needs a permission
            // beyond Publish. Checked here rather than by an endpoint filter
            // because the answer is in the payload, not the route — and checked
            // at all because the studio only *hides* the option, which is not a
            // gate.
            var identities = ReadScriptIdentities(model.BpmnXml);
            if (identities.DeclaresSystem)
            {
                var decision = await authorizer.AuthorizeAsync(
                    actor,
                    Actions.ElevateScript,
                    new EntityRef(EntityKinds.WorkflowModel, id.ToString()),
                    cancellationToken);
                if (decision.Effect != AuthEffect.Allow)
                {
                    return Results.Json(
                        new
                        {
                            message =
                                "This workflow contains a script task set to run as the system, " +
                                "which requires the 'elevatescript' permission on the workflow.",
                        },
                        statusCode: StatusCodes.Status403Forbidden);
                }
            }

            var deployment = await flowable.DeployProcessAsync(model, cancellationToken);
            var published = await store.PublishAsync(model, deployment, cancellationToken);
            // A fresh deployment is always active in Flowable — null out any
            // stale suspended flag so the SPA shows "Pause" rather than "Resume".
            var augmented = published with { IsSuspended = false };
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ModelPublished,
                WorkflowResourceKinds.WorkflowModel,
                resource: new { id = published.Id, name = published.Name, processKey = published.ProcessKey },
                details: new
                {
                    deploymentId = deployment.DeploymentId,
                    processDefinitionId = deployment.ProcessDefinitionId,
                    // #153: which steps were declared to run as something other
                    // than their preceding assignee. A privilege declaration is
                    // worth a record of who published it and when.
                    scriptIdentities = identities.ByElementId,
                },
                cancellationToken);
            return Results.Ok(new PublishResponse(augmented, deployment));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowModel, Actions.Publish, "id");

        group.MapPost("/{processKey}/start", async (
            string processKey,
            StartInstanceRequest? request,
            IFlowableClient flowable,
            IWorkflowModelStore store,
            IAuditEventPublisher auditPublisher,
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

            var model = await store.GetByProcessKeyAsync(processKey, cancellationToken);
            var mergedVariables = MergeDefaultVariables(model?.DefaultVariables, request?.Variables);

            var instance = await flowable.StartProcessInstanceAsync(
                processKey,
                name,
                mergedVariables,
                cancellationToken);
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ModelStarted,
                WorkflowResourceKinds.Execution,
                resource: new { processKey, processInstanceId = instance.Id, name },
                details: new
                {
                    hadVariables = request?.Variables is { Count: > 0 },
                    appliedDefaultsCount = model?.DefaultVariables?.Count ?? 0
                },
                cancellationToken);
            return Results.Ok(instance);
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowModel, Actions.Start, "processKey");

        // Hard delete. Cascades to workflow_model_versions via the FK. Does
        // not undeploy from Flowable — operators are expected to pause +
        // undeploy on the Flowable side first when removing a published
        // workflow, otherwise the deployment lingers and will be re-discovered
        // by tooling that lists Flowable deployments directly.
        group.MapDelete("/{id:guid}", async (
            Guid id,
            IWorkflowModelStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await store.DeleteAsync(id, cancellationToken);
            if (snapshot is null) return Results.NotFound();

            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ModelDeleted,
                WorkflowResourceKinds.WorkflowModel,
                resource: new { id = snapshot.Id, name = snapshot.Name, processKey = snapshot.ProcessKey },
                details: new
                {
                    wasPublished = snapshot.LastDeployment is not null,
                    processDefinitionId = snapshot.LastDeployment?.ProcessDefinitionId
                },
                cancellationToken);
            return Results.NoContent();
        }).RequirePermission(EntityKinds.WorkflowModel, Actions.Delete, "id");

        group.MapPost("/{id:guid}/pause", async (
            Guid id,
            IWorkflowModelStore store,
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
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
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ModelPaused,
                WorkflowResourceKinds.WorkflowModel,
                resource: new { id = model.Id, name = model.Name, processKey = model.ProcessKey },
                details: null,
                cancellationToken);
            return Results.Ok(augmented);
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.WorkflowModel, Actions.Pause, "id");

        group.MapPost("/{id:guid}/resume", async (
            Guid id,
            IWorkflowModelStore store,
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
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
            await auditPublisher.PublishAsync(
                WorkflowAdminEventTopic.TopicName,
                WorkflowAdminEventTypes.ModelResumed,
                WorkflowResourceKinds.WorkflowModel,
                resource: new { id = model.Id, name = model.Name, processKey = model.ProcessKey },
                details: null,
                cancellationToken);
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

    private static Dictionary<string, object?>? MergeDefaultVariables(
        IReadOnlyList<WorkflowDefaultVariable>? defaults,
        IReadOnlyDictionary<string, object?>? overrides)
    {
        var hasDefaults = defaults is { Count: > 0 };
        var hasOverrides = overrides is { Count: > 0 };
        if (!hasDefaults && !hasOverrides)
        {
            return null;
        }

        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (hasDefaults)
        {
            foreach (var variable in defaults!)
            {
                if (string.IsNullOrWhiteSpace(variable.Name)) continue;
                merged[variable.Name] = ConvertDefaultVariableValue(variable);
            }
        }

        if (hasOverrides)
        {
            // Caller-supplied values win over the model's defaults.
            foreach (var (name, value) in overrides!)
            {
                merged[name] = value;
            }
        }

        return merged;
    }

    private static object? ConvertDefaultVariableValue(WorkflowDefaultVariable variable)
    {
        if (variable.Value is not { } element || element.ValueKind == JsonValueKind.Null
            || element.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return variable.Type switch
        {
            "boolean" => element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(element.GetString(), out var b) => b,
                _ => null
            },
            "number" => element.ValueKind switch
            {
                JsonValueKind.Number when element.TryGetInt64(out var i) => i,
                JsonValueKind.Number => element.GetDouble(),
                JsonValueKind.String when double.TryParse(element.GetString(), out var d) => d,
                _ => null
            },
            "string" => element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Null => null,
                _ => element.GetRawText()
            },
            "json" => element,
            _ => element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetInt64(out var i) => i,
                JsonValueKind.Number => element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element
            }
        };
    }

    // Reads the script identity declarations out of a model's BPMN.
    //
    // Tolerant of unparseable XML on purpose: publish already fails downstream
    // with a better message than this would produce, and throwing here would
    // turn a diagnosable validation error into a 500.
    private static (bool DeclaresSystem, IReadOnlyDictionary<string, string> ByElementId)
        ReadScriptIdentities(string? bpmnXml)
    {
        if (string.IsNullOrWhiteSpace(bpmnXml))
        {
            return (false, new Dictionary<string, string>(StringComparer.Ordinal));
        }
        try
        {
            var document = XDocument.Parse(bpmnXml);
            return (
                ScriptTaskIdentity.DeclaresSystemIdentity(document),
                ScriptTaskIdentity.DeclaredIdentities(document));
        }
        catch (System.Xml.XmlException)
        {
            return (false, new Dictionary<string, string>(StringComparer.Ordinal));
        }
    }

}
