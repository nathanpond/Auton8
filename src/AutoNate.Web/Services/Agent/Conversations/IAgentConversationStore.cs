using System.Text.Json;
using AutoNate.Web.Services.Agent.Providers;

namespace AutoNate.Web.Services.Agent.Conversations;

// Storage shape for the agent's persistent transcripts. Per-user, per-page,
// hard-deleted on user request. The agent loop calls these methods to persist
// each user/assistant message and each tool call as it streams.
public interface IAgentConversationStore
{
    Task<AgentConversationDto> CreateAsync(
        Guid userId,
        string pageKey,
        Guid? connectionId,
        string? providerKind,
        string? modelId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentConversationDto>> ListForUserAsync(
        Guid userId,
        string? pageKey,
        int take = 50,
        CancellationToken cancellationToken = default);

    Task<AgentConversationDetailDto?> GetForUserAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<AgentConversationDto?> RenameAsync(
        Guid id,
        Guid userId,
        string title,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    Task<Guid> AppendMessageAsync(
        Guid conversationId,
        ChatRole role,
        IReadOnlyList<ChatContentBlock> blocks,
        string? providerKind,
        string? modelId,
        Usage? usage,
        ChatStopReason? stopReason,
        CancellationToken cancellationToken = default);

    Task<Guid> AppendToolCallAsync(
        Guid messageId,
        string toolUseId,
        string toolName,
        JsonElement args,
        CancellationToken cancellationToken = default);

    Task UpdateToolCallAsync(
        Guid toolCallId,
        string status,
        JsonElement? result,
        string? errorText,
        long durationMs,
        CancellationToken cancellationToken = default);

    // Returns the conversation history for the chat loop. If a "summary"
    // row exists, everything before it is replaced by a single synthetic
    // assistant message holding the summary text — that's how compaction
    // hands a conversation back to the model without losing thread.
    Task<IReadOnlyList<ChatMessage>> LoadMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    // Same as LoadMessagesAsync but pairs each message with its DB id.
    // ConversationCompactor needs the id of the last message it's about to
    // subsume so the persisted summary row can record a precise
    // replaces_through_message_id pointer. The synthetic summary turn is
    // emitted with the summary row's own id (not Guid.Empty) so future
    // re-compactions can chain cleanly.
    Task<IReadOnlyList<LoadedMessage>> LoadMessagesWithIdsAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    // Persists a summary row that subsumes every message older than (and
    // including) replacesThroughMessageId. Returns the new message id.
    // The returned id is also what subsequent loaders match against to
    // know "this prefix was rolled up."
    Task<Guid> AppendSummaryAsync(
        Guid conversationId,
        string summaryText,
        Guid replacesThroughMessageId,
        string? providerKind,
        string? modelId,
        Usage? usage,
        CancellationToken cancellationToken = default);
}

public sealed record class AgentConversationDto(
    Guid Id,
    Guid UserId,
    string PageKey,
    string? Title,
    string? ProviderKind,
    string? ModelId,
    Guid? ConnectionId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? LastMessageAtUtc);

public sealed record class AgentConversationDetailDto(
    AgentConversationDto Conversation,
    IReadOnlyList<AgentMessageDto> Messages,
    IReadOnlyList<AgentToolCallDto> ToolCalls);

public sealed record class AgentMessageDto(
    Guid Id,
    string Role,
    JsonElement Content,
    string? ProviderKind,
    string? ModelId,
    int? InputTokens,
    int? OutputTokens,
    string? StopReason,
    DateTime CreatedAtUtc);

public sealed record class LoadedMessage(Guid Id, ChatMessage Message);

public sealed record class AgentToolCallDto(
    Guid Id,
    Guid MessageId,
    string ToolUseId,
    string ToolName,
    JsonElement Args,
    JsonElement? Result,
    string Status,
    string? ErrorText,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    int? DurationMs);
