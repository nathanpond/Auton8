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
public sealed record class AgentSessionContext(
    ClaimsPrincipal User,
    Guid UserId,
    string PageKey);

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
