using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Forms;
using AutoNate.Web.Services.SiteSettings;
using AutoNate.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Phase 5b — read-only design-surface diagnostics. Bundles forms, workflow
// models, and appearance because the model rarely needs to drill into more
// than one at a time, and a single catalog entry keeps the system-prompt
// token cost down. Dashboards moved to their own LookupDashboardsSkill
// (with widget-level access + the widget-type catalog).
//
// Per-domain auth gates mirror the existing endpoints:
//   - Forms        : IFormStore reads are open to authenticated callers
//   - WorkflowModels: kind-level WorkflowModel:View, per-instance for get_
//   - Appearance   : SiteConfig:view (admin DTO; the public /api/appearance
//                    is anon and not surfaced here — the agent's authenticated
//                    surface should always go through the admin DTO).
public sealed class DesignSurfacesLookupSkill : IAgentSkill
{
    public string Name => "design-surfaces";

    public string Description =>
        "Read-only catalog of design-time entities: forms, workflow models, and site appearance.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public DesignSurfacesLookupSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "list_forms",
                Description: "List forms with publication status.",
                JsonSchema: ParseSchema("""{ "type": "object", "properties": {}, "additionalProperties": false }"""),
                Invoke: InvokeListFormsAsync),

            new AgentTool(
                Name: "get_form",
                Description: "Fetch one form by id or shortCode (provide exactly one).",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "id": { "type": ["string", "null"], "description": "Form GUID." },
                        "shortCode": { "type": ["string", "null"], "description": "Stable short code." }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetFormAsync),

            new AgentTool(
                Name: "list_workflow_models",
                Description: "List workflow models (process definitions). Requires WorkflowModel:view.",
                JsonSchema: ParseSchema("""{ "type": "object", "properties": {}, "additionalProperties": false }"""),
                Invoke: InvokeListWorkflowModelsAsync),

            new AgentTool(
                Name: "get_workflow_model",
                Description: "Fetch one workflow model by id. Requires WorkflowModel:view on the instance.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": { "id": { "type": "string" } },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetWorkflowModelAsync),

            new AgentTool(
                Name: "list_workflow_model_versions",
                Description: "List published versions for a workflow model.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": { "id": { "type": "string" } },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListWorkflowModelVersionsAsync),

            new AgentTool(
                Name: "get_site_appearance",
                Description: "Return the current site appearance (theme, branding, status colors). Requires SiteConfig:view.",
                JsonSchema: ParseSchema("""{ "type": "object", "properties": {}, "additionalProperties": false }"""),
                Invoke: InvokeGetAppearanceAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Design-surface tools are read-only — for in-progress edits (unsaved drafts in the workflow studio, form-builder selection), prefer inspect_page / query_page on the active design page once those page-context providers ship. Authoring writes go through the SPA's existing UIs. Dashboards are covered by the lookup-dashboards / manage-dashboards skills.";

    // ---- forms ------------------------------------------------------------

    private static async Task<JsonElement> InvokeListFormsAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        var store = ctx.Services.GetRequiredService<IFormStore>();
        var rows = await store.ListAsync(ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "forms",
            source = "IFormStore",
            data = rows
        });
    }

    private static async Task<JsonElement> InvokeGetFormAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        var store = ctx.Services.GetRequiredService<IFormStore>();
        var hasId = TryReadGuid(args, "id", out var id);
        var shortCode = ReadString(args, "shortCode");
        if (hasId && !string.IsNullOrEmpty(shortCode))
            return Error("get_form", "Provide exactly one of id or shortCode.");
        if (!hasId && string.IsNullOrEmpty(shortCode))
            return Error("get_form", "One of id or shortCode is required.");
        var form = hasId
            ? await store.GetAsync(id, ct)
            : await store.GetByShortCodeAsync(shortCode!, ct);
        if (form is null) return Error("get_form", "Form not found.");
        return JsonSerializer.SerializeToElement(new
        {
            kind = "form",
            source = "IFormStore",
            data = form
        });
    }

    // ---- workflow models --------------------------------------------------

    private static async Task<bool> CanWorkflowKindAsync(AgentToolContext ctx, CancellationToken ct)
    {
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.WorkflowModel, string.Empty), ct);
        return decision.IsAllowed;
    }

    private static async Task<bool> CanWorkflowAsync(AgentToolContext ctx, Guid id, CancellationToken ct)
    {
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.WorkflowModel, id.ToString()), ct);
        return decision.IsAllowed;
    }

    private static async Task<JsonElement> InvokeListWorkflowModelsAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!await CanWorkflowKindAsync(ctx, ct))
            return Error("list_workflow_models", "WorkflowModel:view required.");
        var store = ctx.Services.GetRequiredService<IWorkflowModelStore>();
        var models = await store.ListAsync(ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "workflow_models",
            source = "IWorkflowModelStore",
            data = models.Select(m => new
            {
                id = m.Id,
                name = m.Name,
                processKey = m.ProcessKey,
                isDraft = m.IsDraft,
                draftVersionNumber = m.DraftVersionNumber,
                publishedVersionNumber = m.PublishedVersionNumber,
                isSuspended = m.IsSuspended,
                createdAtUtc = m.CreatedAtUtc,
                updatedAtUtc = m.UpdatedAtUtc
            }).ToArray()
        });
    }

    private static async Task<JsonElement> InvokeGetWorkflowModelAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!TryReadGuid(args, "id", out var id))
            return Error("get_workflow_model", "id is required and must be a GUID.");
        if (!await CanWorkflowAsync(ctx, id, ct))
            return Error("get_workflow_model", $"WorkflowModel:view required on {id}.");
        var store = ctx.Services.GetRequiredService<IWorkflowModelStore>();
        var model = await store.GetAsync(id, ct);
        if (model is null) return Error("get_workflow_model", $"WorkflowModel {id} not found.");
        return JsonSerializer.SerializeToElement(new
        {
            kind = "workflow_model",
            source = "IWorkflowModelStore",
            data = new
            {
                id = model.Id,
                name = model.Name,
                processKey = model.ProcessKey,
                isDraft = model.IsDraft,
                draftVersionNumber = model.DraftVersionNumber,
                publishedVersionNumber = model.PublishedVersionNumber,
                isSuspended = model.IsSuspended,
                activeProcessInstanceId = model.ActiveProcessInstanceId,
                lastDeployment = model.LastDeployment,
                defaultVariables = model.DefaultVariables,
                bpmnByteCount = model.BpmnXml?.Length ?? 0
            }
        });
    }

    private static async Task<JsonElement> InvokeListWorkflowModelVersionsAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!TryReadGuid(args, "id", out var id))
            return Error("list_workflow_model_versions", "id is required and must be a GUID.");
        if (!await CanWorkflowAsync(ctx, id, ct))
            return Error("list_workflow_model_versions", $"WorkflowModel:view required on {id}.");
        var store = ctx.Services.GetRequiredService<IWorkflowModelStore>();
        var versions = await store.ListVersionsAsync(id, ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "workflow_model_versions",
            source = "IWorkflowModelStore",
            data = versions.Select(v => new
            {
                id = v.Id,
                workflowModelId = v.WorkflowModelId,
                versionNumber = v.VersionNumber,
                name = v.Name,
                processKey = v.ProcessKey,
                deployment = v.Deployment,
                publishedAtUtc = v.PublishedAtUtc
            }).ToArray()
        });
    }

    // ---- appearance -------------------------------------------------------

    private static async Task<JsonElement> InvokeGetAppearanceAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.SiteConfig, string.Empty), ct);
        if (!decision.IsAllowed)
            return Error("get_site_appearance", "SiteConfig:view required.");
        var cache = ctx.Services.GetRequiredService<SiteAppearanceSnapshotCache>();
        var dto = await cache.GetAsync(ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "site_appearance",
            source = "SiteAppearanceSnapshotCache",
            data = dto
        });
    }

    // ---- helpers ----------------------------------------------------------

    private static bool TryReadGuid(JsonElement args, string name, out Guid id)
    {
        id = Guid.Empty;
        if (!args.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String) return false;
        return Guid.TryParse(v.GetString(), out id);
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static JsonElement Error(string source, string message) =>
        JsonSerializer.SerializeToElement(new { kind = "error", source, data = new { message } });

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
