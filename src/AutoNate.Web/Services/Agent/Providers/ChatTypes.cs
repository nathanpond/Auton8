using System.Text.Json;

namespace AutoNate.Web.Services.Agent.Providers;

// Provider-neutral chat types. The agent loop and skills work in these shapes;
// per-provider classes (AnthropicChatProvider, OpenAIChatProvider) translate
// to and from the wire format. Storing a conversation in this shape (in
// agent_message.content_json) lets us replay it against either provider
// without losing tool-call structure.

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool
}

public abstract record class ChatContentBlock
{
    public sealed record class TextBlock(string Text) : ChatContentBlock;

    // The id is the provider-issued correlation id (Anthropic's `tool_use.id`,
    // OpenAI's `tool_calls[i].id`). The store persists it in tool_use_id so we
    // can match tool_result blocks back to invocations.
    public sealed record class ToolUseBlock(string ToolUseId, string Name, JsonElement Args) : ChatContentBlock;

    public sealed record class ToolResultBlock(string ToolUseId, JsonElement Result, bool IsError) : ChatContentBlock;
}

public sealed record class ChatMessage(ChatRole Role, IReadOnlyList<ChatContentBlock> Blocks);

public sealed record class ChatTool(string Name, string Description, JsonElement JsonSchema);

public sealed record class ChatRequest(
    IReadOnlyList<ChatMessage> Messages,
    string? SystemPrompt,
    IReadOnlyList<ChatTool> Tools,
    string ModelId,
    int? MaxTokens = null,
    double? Temperature = null);

public sealed record class Usage(
    int InputTokens,
    int OutputTokens,
    int? CacheReadTokens,
    int? CacheWriteTokens);

public enum ChatStopReason
{
    EndTurn,
    ToolUse,
    MaxTokens,
    StopSequence,
    Error,
    Cancelled
}

public abstract record class ChatStreamChunk
{
    public sealed record class TextDelta(string Delta) : ChatStreamChunk;

    public sealed record class ToolUseStarted(string ToolUseId, string Name) : ChatStreamChunk;

    public sealed record class ToolUseInputDelta(string ToolUseId, string PartialJson) : ChatStreamChunk;

    public sealed record class ToolUseCompleted(string ToolUseId, string Name, JsonElement Args) : ChatStreamChunk;

    public sealed record class MessageStop(ChatStopReason StopReason, Usage? Usage) : ChatStreamChunk;

    public sealed record class Error(string Message, bool IsRetryable) : ChatStreamChunk;
}

public sealed record class ChatProviderTestResult(
    bool Ok,
    long LatencyMs,
    string? ModelEcho,
    string? Error);
