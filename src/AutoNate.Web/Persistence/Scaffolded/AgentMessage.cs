namespace AutoNate.Web.Persistence.Scaffolded;

// One turn in an agent conversation. ContentJson holds provider-neutral
// content blocks ([{type:"text"}, {type:"tool_use"}, {type:"tool_result"}])
// so a message produced by Anthropic can be replayed to OpenAI (and vice
// versa) without translation losing data. ParentMessageId is reserved for
// regenerate / branch flows that aren't shipping in v1 but cost nothing to
// have on the row. Token columns capture provider Usage where available;
// CacheRead/Write are populated only by providers that report prompt-cache
// usage (Anthropic today).
public partial class AgentMessage
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public Guid? ParentMessageId { get; set; }

    public string Role { get; set; } = null!;

    public string ContentJson { get; set; } = "[]";

    public string? ProviderKind { get; set; }

    public string? ModelId { get; set; }

    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    public int? CacheReadTokens { get; set; }

    public int? CacheWriteTokens { get; set; }

    public string? StopReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    // "chat" for normal user/assistant/tool turns, "summary" for synthetic
    // rows produced by ConversationCompactor that replace a prefix of older
    // turns once a conversation outgrows the model's context window. Default
    // is "chat" so existing rows backfill cleanly.
    public string Kind { get; set; } = "chat";

    // Set on summary rows: the last message id whose history was rolled up
    // into this summary. Older rows stay in the table for audit but the
    // chat-loop loader skips them in favour of the summary text.
    public Guid? ReplacesThroughMessageId { get; set; }
}
