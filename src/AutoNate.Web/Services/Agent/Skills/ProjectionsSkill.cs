using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Agent.Skills.Internal;
using AutoNate.Web.Services.Projections;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Phase 5a — projection-framework lookup + operate. Gated on SiteConfig:edit
// to mirror the existing /api/admin/projections endpoints.
public sealed class ProjectionsSkill : IAgentSkill
{
    public string Name => "projections";

    public string Description =>
        "List projection-framework caches, inspect health, and pause / resume / rebuild them (admin-only).";

    public IReadOnlyList<AgentTool> Tools { get; }

    public ProjectionsSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "list_projections",
                Description: "List every registered projection with runtime health (paused flag, last-applied, failures, feed watermarks).",
                JsonSchema: ParseSchema("""
                    { "type": "object", "properties": {}, "additionalProperties": false }
                    """),
                Invoke: InvokeListAsync),

            new AgentTool(
                Name: "get_projection",
                Description: "Fetch one projection's health snapshot by name.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "name": { "type": "string", "description": "Projection name." }
                      },
                      "required": ["name"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeGetAsync),

            new AgentTool(
                Name: "pause_projection",
                Description: "Pause a projection so the worker stops applying batches for it. Reversible — call resume_projection. Confirm-gated.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "name": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["name"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokePauseAsync),

            new AgentTool(
                Name: "resume_projection",
                Description: "Resume a previously paused projection.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "name": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["name"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeResumeAsync),

            new AgentTool(
                Name: "rebuild_projection",
                Description: "Run the projection's registered backfill source from scratch. Long-running; confirm-gated. Returns the row count written.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "name": { "type": "string" },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["name"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeRebuildAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Projection operations require SiteConfig:edit. Pause/resume are reversible; rebuild can write many rows — always confirm.";

    private static async Task<bool> CanAdminAsync(AgentToolContext ctx, CancellationToken ct)
    {
        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Edit, new EntityRef(EntityKinds.SiteConfig, string.Empty), ct);
        return decision.IsAllowed;
    }

    private static async Task<JsonElement> InvokeListAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        if (!await CanAdminAsync(ctx, ct))
            return Error("list_projections", "SiteConfig:edit permission required.");
        var registry = ctx.Services.GetRequiredService<IProjectionRegistry>();
        var health = ctx.Services.GetRequiredService<IProjectionHealthService>();
        var snaps = health.Snapshot(registry.Projections);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "projections",
            source = "IProjectionRegistry",
            data = snaps
        });
    }

    private static async Task<JsonElement> InvokeGetAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        var name = ReadString(args, "name");
        if (string.IsNullOrWhiteSpace(name))
            return Error("get_projection", "name is required.");
        if (!await CanAdminAsync(ctx, ct))
            return Error("get_projection", "SiteConfig:edit permission required.");
        var registry = ctx.Services.GetRequiredService<IProjectionRegistry>();
        var health = ctx.Services.GetRequiredService<IProjectionHealthService>();
        var p = registry.TryGet(name);
        if (p is null) return Error("get_projection", $"Projection '{name}' is not registered.");
        return JsonSerializer.SerializeToElement(new
        {
            kind = "projection",
            source = "IProjectionRegistry",
            data = health.Snapshot(p)
        });
    }

    private static async Task<JsonElement> InvokePauseAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "pause_projection";
        var name = ReadString(args, "name");
        if (string.IsNullOrWhiteSpace(name)) return ConfirmGate.Rejected(action, "name is required.");
        if (!await CanAdminAsync(ctx, ct))
            return ConfirmGate.Rejected(action, "SiteConfig:edit permission required.");
        var registry = ctx.Services.GetRequiredService<IProjectionRegistry>();
        if (registry.TryGet(name) is null)
            return ConfirmGate.Rejected(action, $"Projection '{name}' is not registered.");
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("projection_pause_proposal", action, new { name });
        ctx.Services.GetRequiredService<IProjectionHealthService>().Pause(name);
        return ConfirmGate.Committed("projection_pause_committed", action, new { name, paused = true });
    }

    private static async Task<JsonElement> InvokeResumeAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "resume_projection";
        var name = ReadString(args, "name");
        if (string.IsNullOrWhiteSpace(name)) return ConfirmGate.Rejected(action, "name is required.");
        if (!await CanAdminAsync(ctx, ct))
            return ConfirmGate.Rejected(action, "SiteConfig:edit permission required.");
        var registry = ctx.Services.GetRequiredService<IProjectionRegistry>();
        if (registry.TryGet(name) is null)
            return ConfirmGate.Rejected(action, $"Projection '{name}' is not registered.");
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("projection_resume_proposal", action, new { name });
        ctx.Services.GetRequiredService<IProjectionHealthService>().Resume(name);
        return ConfirmGate.Committed("projection_resume_committed", action, new { name, paused = false });
    }

    private static async Task<JsonElement> InvokeRebuildAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "rebuild_projection";
        var name = ReadString(args, "name");
        if (string.IsNullOrWhiteSpace(name)) return ConfirmGate.Rejected(action, "name is required.");
        if (!await CanAdminAsync(ctx, ct))
            return ConfirmGate.Rejected(action, "SiteConfig:edit permission required.");
        var registry = ctx.Services.GetRequiredService<IProjectionRegistry>();
        if (registry.TryGet(name) is null)
            return ConfirmGate.Rejected(action, $"Projection '{name}' is not registered.");
        if (!ConfirmGate.IsConfirmed(args))
            return ConfirmGate.Proposal("projection_rebuild_proposal", action,
                new { name, note = "Backfill is long-running and may write many rows." });
        var runner = ctx.Services.GetRequiredService<BackfillRunner>();
        try
        {
            var rows = await runner.RunAsync(name, cancellationToken: ct);
            return ConfirmGate.Committed("projection_rebuild_committed", action, new { name, rowsWritten = rows });
        }
        catch (InvalidOperationException ex)
        {
            return ConfirmGate.Failed("projection_rebuild_failed", action, ex.Message);
        }
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
