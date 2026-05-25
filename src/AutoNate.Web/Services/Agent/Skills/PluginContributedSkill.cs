using System.Security.Claims;
using System.Text.Json;
using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Plugins;

namespace AutoNate.Web.Services.Agent.Skills;

// IAgentSkill that aggregates every plugin-contributed tool registered with
// the host's PluginAgentSkillRegistry. Registered ONCE in Program.cs as a
// scoped service; on each per-request construction it snapshots the registry
// so newly-enabled plugins surface in the next conversation turn without a
// host restart.
//
// The Tools list is built per-construction (not lazy) so SkillRegistry's
// duplicate-tool-name check runs against the same view the agent loop will
// dispatch from. Skill metadata (Name / Description) shows up as "plugins"
// in the system prompt with the per-skill fragments concatenated.
//
// Translation of host-private types stays inside this class. The plugin's
// Invoke delegate only ever sees JsonElement + PluginAgentToolContext. The
// CanAsync delegate it receives is a thin wrapper that calls the host's
// IAuthorizer with a ClaimsPrincipal the host owns — the plugin never touches
// ClaimsPrincipal directly (it would drift across ALC unloads).
public sealed class PluginContributedSkill : IAgentSkill
{
    private readonly PluginAgentSkillRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly IReadOnlyList<RegisteredPluginSkillGroup> _snapshot;
    private readonly IReadOnlyList<AgentTool> _tools;

    public PluginContributedSkill(PluginAgentSkillRegistry registry, IServiceProvider services)
    {
        _registry = registry;
        _services = services;
        _snapshot = registry.SnapshotGrouped();
        _tools = _snapshot
            .SelectMany(group => group.Registrations.Select(reg => BuildAgentTool(reg)))
            .ToList();
    }

    // The skill is always present; when no plugin has registered, Tools is
    // empty and SkillRegistry simply finds nothing under this name. The
    // description is informative for the model: it explains where these
    // tools come from so the model can reason about their origin (helpful
    // when an admin asks "what plugins are installed?").
    public string Name => "plugin-skills";

    public string Description =>
        _snapshot.Count == 0
            ? "(No plugin-contributed tools registered.)"
            : "Plugin-contributed tools. " + string.Join("; ", _snapshot.Select(g => $"{g.SkillName}: {g.SkillDescription}"));

    public IReadOnlyList<AgentTool> Tools => _tools;

    public string? SystemPromptFragment(AgentSessionContext context)
    {
        if (_snapshot.Count == 0) return null;
        var fragments = new List<string>();
        var pluginCtx = ToPluginSession(context);
        foreach (var group in _snapshot)
        {
            try
            {
                var f = group.SystemPromptFragment?.Invoke(pluginCtx);
                if (!string.IsNullOrWhiteSpace(f)) fragments.Add(f);
            }
            catch
            {
                // A plugin author throwing from a system-prompt fragment should
                // not break the chat. Skip silently — the tool description on
                // each registered tool is the model's primary contract.
            }
        }
        return fragments.Count == 0 ? null : string.Join("\n", fragments);
    }

    private AgentTool BuildAgentTool(RegisteredPluginSkill reg)
    {
        // Wrap the plugin's Invoke delegate so the host's AgentToolContext is
        // never handed across the ALC boundary. The wrapper:
        //   1. Builds a PluginAgentSessionContext (carries only DTOs / primitives).
        //   2. Invokes the plugin's handler.
        //   3. Returns the JsonElement the plugin produced.
        return new AgentTool(
            Name: reg.Tool.Name,
            Description: reg.Tool.Description,
            JsonSchema: reg.Tool.JsonSchema,
            Invoke: async (args, ctx, ct) =>
            {
                var pluginCtx = new PluginAgentToolContext(ToPluginSession(ctx.Session));
                try
                {
                    return await reg.Tool.Invoke(args, pluginCtx, ct);
                }
                catch (Exception ex)
                {
                    // Plugin code is untrusted relative to the host. Wrap any
                    // thrown exception as an error envelope so a misbehaving
                    // plugin can't crash the whole agent loop.
                    return JsonSerializer.SerializeToElement(new
                    {
                        kind = "error",
                        source = $"plugin:{reg.SkillName}",
                        data = new
                        {
                            message = $"Plugin tool '{reg.Tool.Name}' threw: {ex.Message}"
                        }
                    });
                }
            });
    }

    private PluginAgentSessionContext ToPluginSession(AgentSessionContext session)
    {
        return new PluginAgentSessionContext(
            UserId: session.UserId,
            PageKey: session.PageKey,
            ConversationId: session.ConversationId,
            CanAsync: (kind, action, entityId, ct) =>
                CanAsync(session.User, kind, action, entityId, ct));
    }

    // Authorization probe surfaced to plugins. Resolves the host's IAuthorizer
    // and runs a kind- or per-instance check. Unknown kinds / malformed
    // entity ids return false (deny by default) rather than throw — plugin
    // code shouldn't be able to learn about host internals through error
    // messages here.
    private async Task<bool> CanAsync(
        ClaimsPrincipal actor,
        string kind,
        string action,
        string? entityId,
        CancellationToken ct)
    {
        try
        {
            var authorizer = _services.GetService(typeof(IAuthorizer)) as IAuthorizer;
            if (authorizer is null) return false;
            var decision = await authorizer.AuthorizeAsync(
                actor, action, new EntityRef(kind, entityId ?? string.Empty), ct);
            return decision.IsAllowed;
        }
        catch
        {
            return false;
        }
    }
}
