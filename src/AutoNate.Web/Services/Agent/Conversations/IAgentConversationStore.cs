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

    Task<IReadOnlyList<ChatMessage>> LoadMessagesAsync(
        Guid conversationId,
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
