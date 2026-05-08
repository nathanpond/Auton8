namespace AutoNate.Web.Services.Agent.Loop;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    // How many provider calls a single SendMessage turn can make before the
    // loop emits a synthetic stop. Tool-using turns typically need 2-4 calls
    // (initial -> tools -> final answer); 10 leaves headroom for chained
    // diagnostics without runaway recursion.
    public int MaxIterations { get; set; } = 10;

    // Per-tool timeout in seconds. Skill invocations exceeding this raise an
    // OperationCanceledException, which the loop converts into a tool_result
    // with is_error=true so the model can recover gracefully.
    public int ToolTimeoutSeconds { get; set; } = 30;

    // Default per-turn token budget when the request doesn't specify one.
    public int DefaultMaxTokens { get; set; } = 4096;
}
