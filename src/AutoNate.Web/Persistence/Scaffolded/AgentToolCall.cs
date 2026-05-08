namespace AutoNate.Web.Persistence.Scaffolded;

// One tool invocation requested by the assistant inside a single message.
// Inserted with status='pending' the moment the agent loop sees the tool_use
// block, then updated to succeeded/failed/cancelled/denied once the skill
// returns. Persisting at this granularity lets the UI render an inline
// ToolCallCard that reflects live state, lets audit events for tool.invoked
// / completed / failed correlate to a stable id, and lets the loop recover
// orphan rows on a crash by reading rows still in 'pending' state for closed
// conversations. ArgsJson is redacted of secrets per the skill's contract.
public partial class AgentToolCall
{
    public Guid Id { get; set; }

    public Guid MessageId { get; set; }

    public string ToolUseId { get; set; } = null!;

    public string ToolName { get; set; } = null!;

    public string ArgsJson { get; set; } = "{}";

    public string? ResultJson { get; set; }

    public string Status { get; set; } = "pending";

    public string? ErrorText { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }

    public int? DurationMs { get; set; }
}
