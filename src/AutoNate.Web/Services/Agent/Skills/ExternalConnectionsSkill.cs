using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Agent.Skills.Internal;
using AutoNate.Web.Services.ExternalConnections;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Phase 5a — external-connection lookup + operate (test / enable / disable /
// set-default / delete). Plaintext secret writes are intentionally OUT of
// scope: the chatbot should never carry an API key in a tool-call argument
// (the messages go through model providers' inference pipelines). Admins
// rotate secrets through the SPA's existing form.
public sealed class ExternalConnectionsSkill : IAgentSkill
{
    public string Name => "external-connections";

    public string Description =>
        "List external connections (LLM providers, etc.), test reachability, toggle enabled state, set default. Secret rotation goes through the admin UI.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public ExternalConnectionsSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "list_external_connections",
                Description: "List external connections, optionally filtered by kind (e.g. 'anthropic', 'openai', 'web-search'). Requires ExternalConnection:view.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "kind": { "type": ["string", "null"] }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListAsync),

            new AgentTool(
                Name: "get_external_connection",
                Description: "Fetch one external connection by id (plaintext-free).",
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
                Name: "test_external_connection",
                Description: "Ping the remote provider to confirm credentials and reach. Returns ok/latency/error.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": { "id": { "type": "string" }, "confirmed": { "type": "boolean" } },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeTestAsync),

            new AgentTool(
                Name: "set_external_connection_enabled",
                Description: "Enable or disable a connection. When disabled, dependent integrations stop using it.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "id": { "type": "string" },
                        "enabled": { "type": "boolean" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["id", "enabled"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeSetEnabledAsync),

            new AgentTool(
                Name: "set_default_external_connection",
                Description: "Mark a connection as the default for its kind (e.g. the default LLM provider).",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": { "id": { "type": "string" }, "confirmed": { "type": "boolean" } },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeSetDefaultAsync),

            new AgentTool(
                Name: "delete_external_connection",
                Description: "Delete an external connection. Irreversible. Confirm-gated.",
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
        "External connections hold secrets. NEVER include API keys / passwords in tool-call arguments; secret rotation goes through the admin UI's connection form. List and test freely; toggling enabled or default state requires confirmation.";

    private static async Task<bool> CanViewAsync(AgentToolContext ctx, CancellationToken ct)
    {
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.View, new EntityRef(EntityKinds.ExternalConnection, string.Empty), ct);
        return decision.IsAllowed;
    }

    private static async Task<bool> CanManageAsync(AgentToolContext ctx, string entityId, CancellationToken ct)
    {
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Manage, new EntityRef(EntityKinds.ExternalConnection, entityId), ct);
        return decision.IsAllowed;
    }

    private static async Task<JsonElement> InvokeListAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!await CanViewAsync(ctx, ct))
            return Error("list_external_connections", "ExternalConnection:view required.");
        var kind = ReadString(args, "kind");
        var store = ctx.Services.GetRequiredService<IExternalConnectionStore>();
        var rows = await store.ListAsync(kind, ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "external_connections",
            source = "IExternalConnectionStore",
            data = rows
        });
    }

    private static async Task<JsonElement> InvokeGetAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!TryReadGuid(args, "id", out var id))
            return Error("get_external_connection", "id is required and must be a GUID.");
        if (!await CanViewAsync(ctx, ct))
            return Error("get_external_connection", "ExternalConnection:view required.");
        var store = ctx.Services.GetRequiredService<IExternalConnectionStore>();
        var row = await store.GetAsync(id, ct);
        if (row is null) return Error("get_external_connection", $"Connection {id} not found.");
        return JsonSerializer.SerializeToElement(new
        {
            kind = "external_connection",
            source = "IExternalConnectionStore",
            data = row
        });
    }

    private static async Task<JsonElement> InvokeTestAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "test_external_connection";
        if (!TryReadGuid(args, "id", out var id))
            return ConfirmGate.Rejected(action, "id is required and must be a GUID.");
        if (!await CanManageAsync(ctx, id.ToString(), ct))
            return ConfirmGate.Rejected(action, $"ExternalConnection:manage required on connection {id}.");
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("external_connection_test_proposal", action, new { id });
        var svc = ctx.Services.GetRequiredService<ITestConnectionService>();
        var result = await svc.TestAsync(id, ct);
        return ConfirmGate.Committed("external_connection_test_committed", action, result);
    }

    private static async Task<JsonElement> InvokeSetEnabledAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "set_external_connection_enabled";
        if (!TryReadGuid(args, "id", out var id))
            return ConfirmGate.Rejected(action, "id is required and must be a GUID.");
        if (!args.TryGetProperty("enabled", out var e) || (e.ValueKind != JsonValueKind.True && e.ValueKind != JsonValueKind.False))
            return ConfirmGate.Rejected(action, "enabled (boolean) is required.");
        var enabled = e.GetBoolean();
        if (!await CanManageAsync(ctx, id.ToString(), ct))
            return ConfirmGate.Rejected(action, $"ExternalConnection:manage required on connection {id}.");
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("external_connection_set_enabled_proposal", action, new { id, enabled });
        var store = ctx.Services.GetRequiredService<IExternalConnectionStore>();
        var row = await store.UpdateAsync(id, new UpdateExternalConnectionInput(
            Name: null, Description: null, IsEnabled: enabled,
            Metadata: null, Secret: null), ctx.Session.UserId, ct);
        if (row is null) return ConfirmGate.Failed("external_connection_set_enabled_failed", action, $"Connection {id} not found.");
        return ConfirmGate.Committed("external_connection_set_enabled_committed", action, row);
    }

    private static async Task<JsonElement> InvokeSetDefaultAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "set_default_external_connection";
        if (!TryReadGuid(args, "id", out var id))
            return ConfirmGate.Rejected(action, "id is required and must be a GUID.");
        if (!await CanManageAsync(ctx, id.ToString(), ct))
            return ConfirmGate.Rejected(action, $"ExternalConnection:manage required on connection {id}.");
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("external_connection_set_default_proposal", action, new { id });
        var store = ctx.Services.GetRequiredService<IExternalConnectionStore>();
        var row = await store.SetDefaultAsync(id, ctx.Session.UserId, ct);
        if (row is null) return ConfirmGate.Failed("external_connection_set_default_failed", action, $"Connection {id} not found.");
        return ConfirmGate.Committed("external_connection_set_default_committed", action, row);
    }

    private static async Task<JsonElement> InvokeDeleteAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "delete_external_connection";
        if (!TryReadGuid(args, "id", out var id))
            return ConfirmGate.Rejected(action, "id is required and must be a GUID.");
        if (!await CanManageAsync(ctx, id.ToString(), ct))
            return ConfirmGate.Rejected(action, $"ExternalConnection:manage required on connection {id}.");
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("external_connection_delete_proposal", action,
                new { id, warning = "Irreversible." });
        var store = ctx.Services.GetRequiredService<IExternalConnectionStore>();
        var ok = await store.DeleteAsync(id, ctx.Session.UserId, ct);
        return ok
            ? ConfirmGate.Committed("external_connection_delete_committed", action, new { id })
            : ConfirmGate.Failed("external_connection_delete_failed", action, $"Connection {id} not found.");
    }

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
