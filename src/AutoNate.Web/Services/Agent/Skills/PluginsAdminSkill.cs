using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Plugins;
using AutoNate.Web.Services.Agent.Skills.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Phase 5a — plugin admin (list / enable / disable / delete). Gated by
// Plugin:manage per AdminPluginsEndpoints. Upload-from-zip is intentionally
// out of scope (multipart upload through the chatbot has no clean shape);
// enable / disable / delete cover the bulk of operator needs.
public sealed class PluginsAdminSkill : IAgentSkill
{
    public string Name => "plugins-admin";

    public string Description =>
        "List installed plugins; enable, disable, or delete one (admin-only). Plugin upload happens via the admin UI.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public PluginsAdminSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "list_plugins",
                Description: "List installed plugins with status (Enabled / Disabled / Failed / Uploaded).",
                JsonSchema: ParseSchema("""{ "type": "object", "properties": {}, "additionalProperties": false }"""),
                Invoke: InvokeListAsync),

            new AgentTool(
                Name: "get_plugin",
                Description: "Fetch one plugin by id.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": { "id": { "type": "string" } },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetAsync),

            new AgentTool(
                Name: "enable_plugin",
                Description: "Enable an installed plugin. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": { "id": { "type": "string" }, "confirmed": { "type": "boolean" } },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeEnableAsync),

            new AgentTool(
                Name: "disable_plugin",
                Description: "Disable a running plugin. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": { "id": { "type": "string" }, "confirmed": { "type": "boolean" } },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeDisableAsync),

            new AgentTool(
                Name: "delete_plugin",
                Description: "Delete a plugin from the host entirely. Drops the per-plugin schema and removes registered menu/behavior/projection rows. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": { "id": { "type": "string" }, "confirmed": { "type": "boolean" } },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeDeleteAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Plugin admin requires Plugin:manage. delete_plugin is irreversible — always confirm; the plugin's schema and data are removed.";

    private static async Task<bool> CanManageAsync(AgentToolContext ctx, CancellationToken ct)
    {
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Manage, new EntityRef(EntityKinds.Plugin, string.Empty), ct);
        return decision.IsAllowed;
    }

    private static async Task<JsonElement> InvokeListAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!await CanManageAsync(ctx, ct))
            return Error("list_plugins", "Plugin:manage permission required.");
        var svc = ctx.Services.GetRequiredService<IPluginManagementService>();
        var plugins = await svc.ListAsync(ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "plugins",
            source = "IPluginManagementService",
            data = plugins
        });
    }

    private static async Task<JsonElement> InvokeGetAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!TryReadGuid(args, "id", out var id))
            return Error("get_plugin", "id is required and must be a GUID.");
        if (!await CanManageAsync(ctx, ct))
            return Error("get_plugin", "Plugin:manage permission required.");
        var svc = ctx.Services.GetRequiredService<IPluginManagementService>();
        var p = await svc.GetAsync(id, ct);
        if (p is null) return Error("get_plugin", $"Plugin {id} not found.");
        return JsonSerializer.SerializeToElement(new
        {
            kind = "plugin",
            source = "IPluginManagementService",
            data = p
        });
    }

    private static async Task<JsonElement> InvokeEnableAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "enable_plugin";
        if (!TryReadGuid(args, "id", out var id)) return ConfirmGate.Rejected(action, "id is required and must be a GUID.");
        if (!await CanManageAsync(ctx, ct)) return ConfirmGate.Rejected(action, "Plugin:manage required.");
        if (!ConfirmGate.IsConfirmed(args)) return ConfirmGate.Proposal("plugin_enable_proposal", action, new { id });
        var svc = ctx.Services.GetRequiredService<IPluginManagementService>();
        var outcome = await svc.EnableAsync(id, ctx.Session.UserId, ct);
        return outcome.Success
            ? ConfirmGate.Committed("plugin_enable_committed", action, outcome.Plugin!)
            : ConfirmGate.Failed("plugin_enable_failed", action, outcome.ErrorMessage ?? outcome.ErrorCode ?? "unknown");
    }

    private static async Task<JsonElement> InvokeDisableAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "disable_plugin";
        if (!TryReadGuid(args, "id", out var id)) return ConfirmGate.Rejected(action, "id is required and must be a GUID.");
        if (!await CanManageAsync(ctx, ct)) return ConfirmGate.Rejected(action, "Plugin:manage required.");
        if (!ConfirmGate.IsConfirmed(args)) return ConfirmGate.Proposal("plugin_disable_proposal", action, new { id });
        var svc = ctx.Services.GetRequiredService<IPluginManagementService>();
        var outcome = await svc.DisableAsync(id, ctx.Session.UserId, ct);
        return outcome.Success
            ? ConfirmGate.Committed("plugin_disable_committed", action, outcome.Plugin!)
            : ConfirmGate.Failed("plugin_disable_failed", action, outcome.ErrorMessage ?? outcome.ErrorCode ?? "unknown");
    }

    private static async Task<JsonElement> InvokeDeleteAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "delete_plugin";
        if (!TryReadGuid(args, "id", out var id)) return ConfirmGate.Rejected(action, "id is required and must be a GUID.");
        if (!await CanManageAsync(ctx, ct)) return ConfirmGate.Rejected(action, "Plugin:manage required.");
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("plugin_delete_proposal", action,
                new { id, warning = "Irreversible: drops the per-plugin schema and removes all menu/behavior/projection rows." });
        var svc = ctx.Services.GetRequiredService<IPluginManagementService>();
        var outcome = await svc.DeleteAsync(id, ctx.Session.UserId, ct);
        if (outcome.ErrorCode == "not_found") return ConfirmGate.Failed("plugin_delete_failed", action, $"Plugin {id} not found.");
        return outcome.Success
            ? ConfirmGate.Committed("plugin_delete_committed", action, new { id })
            : ConfirmGate.Failed("plugin_delete_failed", action, outcome.ErrorMessage ?? outcome.ErrorCode ?? "unknown");
    }

    private static bool TryReadGuid(JsonElement args, string name, out Guid id)
    {
        id = Guid.Empty;
        if (!args.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String) return false;
        return Guid.TryParse(v.GetString(), out id);
    }

    private static JsonElement Error(string source, string message) =>
        JsonSerializer.SerializeToElement(new { kind = "error", source, data = new { message } });

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
