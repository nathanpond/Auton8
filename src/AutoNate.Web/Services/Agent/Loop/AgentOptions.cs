namespace AutoNate.Web.Services.Agent.Loop;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    // How many provider calls a single SendMessage turn can make before the
    // loop emits a synthetic stop. Composite tasks like "build a dashboard
    // with N widgets" need a confirm-gated proposal + commit pair per mutation
    // tool, so a 4-widget build is already ~14 calls before any discovery.
    // 25 absorbs that plus normal grammar/schema lookups without inviting
    // runaway recursion.
    public int MaxIterations { get; set; } = 25;

    // Per-tool timeout in seconds. Skill invocations exceeding this raise an
    // OperationCanceledException, which the loop converts into a tool_result
    // with is_error=true so the model can recover gracefully.
    public int ToolTimeoutSeconds { get; set; } = 30;

    // Default per-turn token budget when the request doesn't specify one.
    public int DefaultMaxTokens { get; set; } = 4096;
}
