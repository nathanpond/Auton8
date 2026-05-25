using System.Text.Json;

namespace AutoNate.Plugins.Abstractions;

// Plugin-facing surface for contributing chatbot tools. Each registered tool
// becomes part of the host's tool catalog the next time SkillRegistry is
// instantiated (per-request scope, so a fresh enable surfaces immediately).
//
// ALC discipline: every type that crosses the host/plugin boundary lives in
// this assembly OR is `JsonElement` / primitives. Plugin code MUST NOT see
// host-private types (IAuthorizer, AgentSessionContext, EF entities) because
// they would be reloaded into the plugin's collectible ALC and fail
// `is`/`as` checks. The PluginAgentToolContext below is the trust boundary —
// the host adapter resolves its own services and only hands the plugin what
// the contract advertises.
//
// Skills registered through this surface are tagged with the plugin's id and
// auto-removed on disable (same lifecycle as IPluginMenus / IPluginBehaviors).
public interface IPluginAgentSkills
{
    // Register one or more tools under a named skill. The skill name appears
    // in the host's system prompt as a logical grouping; tool names are what
    // the model invokes. Skill names and tool names must be globally unique
    // across plugins and the host's built-in catalog — collisions throw.
    void Register(
        string skillName,
        string skillDescription,
        IReadOnlyList<PluginAgentTool> tools,
        Func<PluginAgentSessionContext, string?>? systemPromptFragment = null);

    // Sweep every skill this plugin registered. Mirrors IPluginProjections.
    // Called by the host on plugin disable.
    int RemoveAll();
}

// One tool the plugin advertises to the chatbot.
//   - Name + Description: model-facing identifiers and contract. The
//     Description IS the contract — say what each arg means and any
//     preconditions.
//   - JsonSchema: standard JSON Schema object describing the args shape.
//   - Invoke: async handler. Args land as JsonElement; the handler returns
//     JsonElement (typically an envelope shaped { kind, source, data }).
public sealed record PluginAgentTool(
    string Name,
    string Description,
    JsonElement JsonSchema,
    Func<JsonElement, PluginAgentToolContext, CancellationToken, Task<JsonElement>> Invoke);

// Per-tool-call context handed to the plugin. Exposes only what the boundary
// allows: the calling user's id and a CanAsync delegate that proxies the
// host's authorizer. The plugin's own data surface (per-plugin Postgres
// schema) is reachable through `IPluginContext.Data`, captured by the plugin
// in its Configure() closure — it is not exposed here to keep this DTO ALC-
// safe (closures over host types stay inside the plugin's ALC).
public sealed record PluginAgentToolContext(
    PluginAgentSessionContext Session);

// Session-level context for the plugin. Cross-tool-call within one
// conversation turn — same shape passed to Invoke and to systemPromptFragment.
public sealed record PluginAgentSessionContext(
    Guid UserId,
    string PageKey,
    Guid ConversationId,
    // Authorization probe. Mirrors IAuthorizer.AuthorizeAsync for kinds the
    // host knows about. Returns false for unknown kinds; never throws.
    // entityId may be null for kind-level checks.
    Func<string /*kind*/, string /*action*/, string? /*entityId*/, CancellationToken, Task<bool>> CanAsync);
