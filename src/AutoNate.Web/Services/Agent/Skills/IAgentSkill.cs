using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Services.Agent.Providers;

namespace AutoNate.Web.Services.Agent.Skills;

// A skill is a named bundle of related tools the agent can call. The Phase-5
// skills (explain-workflow, lookup-records, analyze-system-issue) are read-only
// diagnostics; later phases may introduce mutating skills, but the contract is
// the same. Skills register through DI as IAgentSkill and the SkillRegistry
// aggregates them for the agent loop.
public interface IAgentSkill
{
    string Name { get; }

    string Description { get; }

    IReadOnlyList<AgentTool> Tools { get; }

    // Optional system-prompt fragment contributed by the skill — e.g. a hint
    // like "When asked about a workflow, prefer find_workflow first; only call
    // explain_workflow once you have an id." Returning null keeps the prompt
    // shorter when the skill has nothing context-specific to add.
    string? SystemPromptFragment(AgentSessionContext context);
}

// Per-turn context the agent loop hands to skills. The principal is the only
// authorization source — skills MUST route reads through stores that already
// gate by IAuthorizer, never query the DbContext directly for gated entities.
//
// PageContext (optional) is the structured snapshot the SPA bundled with the
// user's message. When non-null, two affordances are advertised to the model:
// inspect_page (cheap snapshot read) and query_page (round-trip to the live
// page). Skills that don't care about page state simply ignore this field.
public sealed record class AgentSessionContext(
    ClaimsPrincipal User,
    Guid UserId,
    string PageKey,
    Guid ConversationId = default,
    PageContextSnapshot? PageContext = null);

// Server-side received view of a SPA-supplied page snapshot. Lives in the
// Skills namespace so any skill can read it via AgentSessionContext without
// importing endpoint types.
public sealed record class PageContextSnapshot(
    string PageKey,
    int SchemaVersion,
    string? Summary,
    long Version,
    JsonElement Data);

public sealed record class AgentTool(
    string Name,
    string Description,
    JsonElement JsonSchema,
    Func<JsonElement, AgentToolContext, CancellationToken, Task<JsonElement>> Invoke);

public sealed record class AgentToolContext(
    AgentSessionContext Session,
    IServiceProvider Services);

// Convenience for skills that produce a simple ChatTool from their declared
// AgentTool list (used by the agent loop when assembling the request).
public static class AgentToolExtensions
{
    public static ChatTool ToChatTool(this AgentTool tool) =>
        new(tool.Name, tool.Description, tool.JsonSchema);
}
