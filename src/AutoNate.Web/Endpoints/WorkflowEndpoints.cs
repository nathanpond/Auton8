using System.Text.RegularExpressions;
using System.Security.Claims;
using System.Text.Json;
using System.Xml.Linq;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Workflow.Behaviors;
using Microsoft.Extensions.Options;
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

        // #166. What a child process declares, so a parent's call activity can
        // offer real mapping targets instead of a free-text box the author has to
        // remember the child's variable names for.
        group.MapGet("/{processKey}/declarations", async (
            string processKey,
            IWorkflowModelStore store,
            CancellationToken cancellationToken) =>
        {
            var model = await store.GetByProcessKeyAsync(processKey, cancellationToken);
            if (model is null)
            {
                // Not an error: a parent may name a child that is not published
                // yet, and publish already refuses that with a better message.
                return Results.Ok(Array.Empty<WorkflowDataDeclaration>());
            }

            return Results.Ok(WorkflowBpmnXml.ExtractDataDeclarations(model.BpmnXml));
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

        // #194: which saved models were written against the API #147 removed.
        //
        // #151 protects everything authored from now on; this is for the
        // diagrams that were already published, which otherwise fail at
        // runtime with nothing having warned anyone. Reads script bodies, so
        // it carries the same View gate the detail endpoint does.
        group.MapGet("/legacy-scripts", async (
            IWorkflowModelStore store,
            CancellationToken cancellationToken) =>
        {
            var models = await store.ListAsync(cancellationToken);
            var affected = models
                .Select(m => new LegacyScriptInventory.ModelFindings(
                    m.Id, m.Name, m.ProcessKey, !m.IsDraft,
                    LegacyScriptInventory.Scan(m.BpmnXml)))
                .Where(r => r.Findings.Count > 0)
                .OrderByDescending(r => r.IsPublished)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Results.Ok(new
            {
                scanned = models.Count,
                affected = affected.Length,
                // Published models are the urgent ones: they are deployed and
                // will fail on their next run. A draft fails only when someone
                // tries to publish it, where #151 explains why.
                publishedAffected = affected.Count(r => r.IsPublished),
                models = affected,
            });
        }).RequireKindPermission(EntityKinds.WorkflowModel, Actions.View);

        group.MapPost("/{id:guid}/publish", async (
            Guid id,
            WorkflowModel model,
            IWorkflowModelStore store,
            IFlowableClient flowable,
            IAuditEventPublisher auditPublisher,
            IAuthorizer authorizer,
            IOptions<WorkflowBehaviorOptions> behaviorOptions,
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

            // #225. The FULL validation set, at the endpoint that actually
            // deploys.
            //
            // It used to be the small promoted subset, because prepare was where
            // validation lived and the studio happens to call prepare first. A
            // direct API caller does not, so every rule written as a gate was
            // advisory — an unsupported element, a subprocess with no start
            // event, a timer boundary that never fires, all reached the engine
            // unchecked.
            //
            // This is a real contract change and was measured before being made:
            // 4 of the 11 models in the dev database are newly refused, every one
            // for a defect that already fails at run time (the script API #147
            // removed, and #153's unresolvable identity). It converts a silent
            // runtime failure into a loud publish-time one.
            //
            // Run on the STORED xml, before expansion, so an author is told about
            // the element they drew rather than one publish generated.
            var validationErrors = WorkflowBpmnXml.ValidateProcess(model.BpmnXml).Errors;
            if (validationErrors.Count > 0)
            {
                return Results.BadRequest(new { errors = validationErrors });
            }

            // #113. Every call activity is resolved to the child definition that
            // exists RIGHT NOW and pinned to it by id.
            //
            // Flowable resolves a calledElement key at run time, to the latest
            // version — verified: an unchanged, already-deployed parent picked up
            // a child version published after it. A running process must not
            // change behaviour underneath its owner, so publish pins instead.
            //
            // A key resolving to nothing is refused here rather than deployed.
            // Flowable accepts such a diagram happily and fails only when an
            // instance reaches the call, by which time it is someone else's
            // problem at the worst moment.
            var callTargets = WorkflowBpmnXml.ExtractCallActivityTargets(model.BpmnXml);
            var definitionIdsByKey = new Dictionary<string, string>(StringComparer.Ordinal);
            var unresolved = new List<string>();
            foreach (var (elementId, calledKey) in callTargets)
            {
                if (definitionIdsByKey.ContainsKey(calledKey)) continue;

                var child = await flowable.GetLatestProcessDefinitionAsync(calledKey, cancellationToken);
                if (child is null || string.IsNullOrWhiteSpace(child.Id))
                {
                    unresolved.Add(
                        $"The step '{elementId}' calls a workflow with key '{calledKey}', and no " +
                        "published workflow has that key. Publish that workflow first, or pick a " +
                        "different one — published as-is, this process fails when it reaches that " +
                        "step rather than now.");
                    continue;
                }

                definitionIdsByKey[calledKey] = child.Id;
            }

            if (unresolved.Count > 0)
            {
                return Results.BadRequest(new { errors = unresolved });
            }

            // #112. Expanded at DEPLOY, not at save. Flowable rejects an
            // intermediate throw (Message) outright and silently ignores a message
            // end event, so the deployed copy carries service tasks on the
            // behaviour bridge instead — while the stored model keeps the diagram
            // the author drew, which is what the send behaviour reads its message
            // name and target back from.
            //
            // Here rather than in the prepare step because prepare's output is
            // what the studio saves, and because a caller that publishes without
            // preparing must not be able to deploy something the engine refuses.
            var deployable = model with
            {
                BpmnXml = WorkflowBpmnXml.StampCallbackBaseUrl(
                    WorkflowBpmnXml.PinCallActivityTargets(
                        WorkflowBpmnXml.ExpandForDeployment(model.BpmnXml),
                        definitionIdsByKey),
                    // #223. Unset in production; nothing is stamped and the engine
                    // uses its own configured callback URL.
                    behaviorOptions.Value.CallbackBaseUrlOverride)
            };

            WorkflowDeploymentInfo deployment;
            try
            {
                deployment = await flowable.DeployProcessAsync(deployable, cancellationToken);
            }
            catch (FlowableRequestException exception) when (exception.IsCallerError)
            {
                // #334. Publish validation catches what Auton8 knows about, but the
                // engine will always refuse things we do not predict -- and what the
                // author used to get for those was a 500 carrying a raw
                // FlowableRequestException, a Java stack trace and absolute file
                // paths, straight to the browser.
                //
                // A 500 also pages someone, for a diagram that is simply wrong.
                // Flowable already classified this as a caller error; the status
                // should say so, and the body should carry the engine's own
                // sentence rather than its call stack.
                return Results.BadRequest(new { errors = new[] { DescribeEngineRefusal(exception) } });
            }

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

    /// <summary>
    /// Flowable's refusal, as a sentence rather than a stack trace (#334).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deployment refusals come back in a machine-readable shape:
    /// </para>
    /// <para>
    /// <code>
    ///   [Validation set: 'flowable-executable-process'
    ///    | Problem: 'flowable-servicetask-missing-implementation']
    ///   : Service task does not have an implementation defined - [Extra info : ...
    /// </code>
    /// </para>
    /// <para>
    /// The problem code is the useful half — it is stable, searchable, and names
    /// the actual constraint — so it is kept verbatim alongside whatever prose
    /// follows. Everything after the first <c>- [Extra info</c> is dropped: that is
    /// where the element ids, line numbers and absolute file paths live.
    /// </para>
    /// <para>
    /// If the message does not match the expected shape the whole thing is passed
    /// through, trimmed. An unrecognised refusal is still better read than
    /// swallowed — the failure mode this milestone keeps finding is silence, not
    /// noise.
    /// </para>
    /// </remarks>
    internal static string DescribeEngineRefusal(FlowableRequestException exception)
    {
        var message = exception.Message ?? string.Empty;

        // Drop the diagnostic tail, which carries ids and filesystem paths.
        var extra = message.IndexOf("- [Extra info", StringComparison.OrdinalIgnoreCase);
        if (extra > 0) message = message[..extra];

        var problem = Regex.Match(message, @"Problem:\s*'(?<code>[^']+)'");
        var prose = Regex.Match(message, @"\]\s*:\s*(?<text>.+)", RegexOptions.Singleline);

        var sentence = prose.Success ? prose.Groups["text"].Value.Trim() : message.Trim();

        // A stack trace starts at the first frame marker. Everything from there on
        // is internals, and this is the path an UNRECOGNISED refusal takes -- so it
        // is exactly where a raw Java dump would otherwise get through.
        var frame = sentence.IndexOf("\tat ", StringComparison.Ordinal);
        if (frame < 0) frame = sentence.IndexOf("\n\tat", StringComparison.Ordinal);
        if (frame > 0) sentence = sentence[..frame];

        // One line. Multi-line engine messages are message-plus-trace.
        var newline = sentence.IndexOfAny(['\r', '\n']);
        if (newline > 0) sentence = sentence[..newline];

        // And no absolute path, on any branch. This is the leak half of #334:
        // filesystem paths must not reach a browser however readable the rest is.
        sentence = Regex.Replace(sentence, @"(?<![\w.])[/\\](?:[\w.\-]+[/\\])+[\w.\-]*", "<path>");

        sentence = Regex.Replace(sentence, @"\s+", " ").Trim();

        if (sentence.Length == 0)
        {
            sentence = "Flowable refused the deployment and gave no reason.";
        }

        return problem.Success
            ? $"The workflow engine refused this workflow: {sentence} ({problem.Groups["code"].Value})"
            : $"The workflow engine refused this workflow: {sentence}";
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
