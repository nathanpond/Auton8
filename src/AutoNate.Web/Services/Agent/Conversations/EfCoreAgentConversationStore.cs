using System.Text.Json;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Agent.Providers;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Agent.Conversations;

public sealed class EfCoreAgentConversationStore : IAgentConversationStore
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbContextFactory;
    private readonly IAuditEventPublisher _auditPublisher;

    public EfCoreAgentConversationStore(
        IDbContextFactory<AutoNateDbContext> dbContextFactory,
        IAuditEventPublisher auditPublisher)
    {
        _dbContextFactory = dbContextFactory;
        _auditPublisher = auditPublisher;
    }

    public async Task<AgentConversationDto> CreateAsync(
        Guid userId,
        string pageKey,
        Guid? connectionId,
        string? providerKind,
        string? modelId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var entity = new AgentConversation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PageKey = pageKey,
            Title = null,
            ProviderKind = providerKind,
            ModelId = modelId,
            ConnectionId = connectionId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            LastMessageAtUtc = null
        };
        dbContext.AgentConversations.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await _auditPublisher.PublishAsync(
            AgentEventTopic.TopicName,
            AgentEventTypes.ConversationCreated,
            AgentResourceKinds.Conversation,
            resource: new { id = entity.Id, userId, pageKey },
            details: new { providerKind, modelId, connectionId },
            cancellationToken);

        return ToConversationDto(entity);
    }

    public async Task<IReadOnlyList<AgentConversationDto>> ListForUserAsync(
        Guid userId,
        string? pageKey,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.AgentConversations
            .AsNoTracking()
            .Where(c => c.UserId == userId);
        if (!string.IsNullOrWhiteSpace(pageKey))
        {
            query = query.Where(c => c.PageKey == pageKey);
        }

        var rows = await query
            .OrderByDescending(c => c.LastMessageAtUtc ?? c.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        await _auditPublisher.PublishAsync(
            AgentEventTopic.TopicName,
            AgentEventTypes.ConversationListViewed,
            AgentResourceKinds.Conversation,
            resource: new { userId, pageKey },
            details: new { count = rows.Count },
            cancellationToken);

        return rows.Select(ToConversationDto).ToList();
    }

    public async Task<AgentConversationDetailDto?> GetForUserAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.AgentConversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken);
        if (entity is null) return null;

        var messages = await dbContext.AgentMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == id)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var messageIds = messages.Select(m => m.Id).ToList();
        var toolCalls = await dbContext.AgentToolCalls
            .AsNoTracking()
            .Where(tc => messageIds.Contains(tc.MessageId))
            .OrderBy(tc => tc.StartedAtUtc)
            .ToListAsync(cancellationToken);

        await _auditPublisher.PublishAsync(
            AgentEventTopic.TopicName,
            AgentEventTypes.ConversationViewed,
            AgentResourceKinds.Conversation,
            resource: new { id, userId },
            details: null,
            cancellationToken);

        return new AgentConversationDetailDto(
            ToConversationDto(entity),
            messages.Select(ToMessageDto).ToList(),
            toolCalls.Select(ToToolCallDto).ToList());
    }

    public async Task<AgentConversationDto?> GetHeaderForUserAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.AgentConversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken);
        return entity is null ? null : ToConversationDto(entity);
    }

    public async Task<AgentConversationDto?> RenameAsync(
        Guid id,
        Guid userId,
        string title,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.AgentConversations
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken);
        if (entity is null) return null;

        entity.Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await _auditPublisher.PublishAsync(
            AgentEventTopic.TopicName,
            AgentEventTypes.ConversationRenamed,
            AgentResourceKinds.Conversation,
            resource: new { id, userId },
            details: new { title = entity.Title },
            cancellationToken);

        return ToConversationDto(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.AgentConversations
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken);
        if (entity is null) return false;

        // Cascade-delete via FK ON DELETE CASCADE handles messages + tool calls.
        dbContext.AgentConversations.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await _auditPublisher.PublishAsync(
            AgentEventTopic.TopicName,
            AgentEventTypes.ConversationDeleted,
            AgentResourceKinds.Conversation,
            resource: new { id, userId },
            details: null,
            cancellationToken);

        return true;
    }

    public async Task<Guid> AppendMessageAsync(
        Guid conversationId,
        ChatRole role,
        IReadOnlyList<ChatContentBlock> blocks,
        string? providerKind,
        string? modelId,
        Usage? usage,
        ChatStopReason? stopReason,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var entity = new AgentMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            ParentMessageId = null,
            Role = role.ToString().ToLowerInvariant(),
            ContentJson = JsonSerializer.Serialize(SerializeBlocks(blocks)),
            ProviderKind = providerKind,
            ModelId = modelId,
            InputTokens = usage?.InputTokens,
            OutputTokens = usage?.OutputTokens,
            CacheReadTokens = usage?.CacheReadTokens,
            CacheWriteTokens = usage?.CacheWriteTokens,
            StopReason = stopReason?.ToString().ToLowerInvariant(),
            CreatedAtUtc = now
        };
        dbContext.AgentMessages.Add(entity);

        // Bump conversation timestamps.
        var convo = await dbContext.AgentConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (convo is not null)
        {
            convo.LastMessageAtUtc = now;
            convo.UpdatedAtUtc = now;
            // First user message becomes the title (truncated).
            if (string.IsNullOrEmpty(convo.Title) && role == ChatRole.User)
            {
                var firstText = blocks.OfType<ChatContentBlock.TextBlock>().FirstOrDefault()?.Text;
                if (!string.IsNullOrWhiteSpace(firstText))
                {
                    convo.Title = firstText.Length > 80 ? firstText[..80] : firstText;
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<Guid> AppendToolCallAsync(
        Guid messageId,
        string toolUseId,
        string toolName,
        JsonElement args,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = new AgentToolCall
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            ToolUseId = toolUseId,
            ToolName = toolName,
            ArgsJson = JsonSerializer.Serialize(args),
            ResultJson = null,
            Status = "pending",
            ErrorText = null,
            StartedAtUtc = DateTime.UtcNow,
            FinishedAtUtc = null,
            DurationMs = null
        };
        dbContext.AgentToolCalls.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task UpdateToolCallAsync(
        Guid toolCallId,
        string status,
        JsonElement? result,
        string? errorText,
        long durationMs,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.AgentToolCalls
            .FirstOrDefaultAsync(tc => tc.Id == toolCallId, cancellationToken);
        if (entity is null) return;

        entity.Status = status;
        entity.ResultJson = result is JsonElement el ? JsonSerializer.Serialize(el) : null;
        entity.ErrorText = errorText;
        entity.FinishedAtUtc = DateTime.UtcNow;
        entity.DurationMs = (int)Math.Min(int.MaxValue, durationMs);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMessage>> LoadMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadMessagesWithIdsAsync(conversationId, cancellationToken);
        var messages = new ChatMessage[loaded.Count];
        for (var i = 0; i < loaded.Count; i++) messages[i] = loaded[i].Message;
        return messages;
    }

    public async Task<IReadOnlyList<LoadedMessage>> LoadMessagesWithIdsAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await dbContext.AgentMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        // Find the most recent summary row. Everything older than (and
        // including) the message it subsumes drops out; the summary itself
        // becomes a synthetic assistant turn so the model sees "previously
        // we discussed: …" before the live tail.
        AgentMessage? latestSummary = null;
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (string.Equals(rows[i].Kind, "summary", StringComparison.OrdinalIgnoreCase))
            {
                latestSummary = rows[i];
                break;
            }
        }

        var loaded = new List<LoadedMessage>(rows.Count);
        if (latestSummary is not null)
        {
            // Synthesize the assistant turn. The actual text lives inside
            // the summary row's ContentJson (a single text block). Carry the
            // summary row's own id so re-compaction can chain through it.
            loaded.Add(new LoadedMessage(
                latestSummary.Id,
                new ChatMessage(ChatRole.Assistant, DeserializeBlocks(latestSummary.ContentJson))));
            foreach (var row in rows)
            {
                if (row.CreatedAtUtc <= latestSummary.CreatedAtUtc) continue;
                if (string.Equals(row.Kind, "summary", StringComparison.OrdinalIgnoreCase)) continue;
                loaded.Add(new LoadedMessage(
                    row.Id,
                    new ChatMessage(ParseRole(row.Role), DeserializeBlocks(row.ContentJson))));
            }
        }
        else
        {
            foreach (var row in rows)
            {
                loaded.Add(new LoadedMessage(
                    row.Id,
                    new ChatMessage(ParseRole(row.Role), DeserializeBlocks(row.ContentJson))));
            }
        }
        return loaded;
    }

    public async Task<Guid> AppendSummaryAsync(
        Guid conversationId,
        string summaryText,
        Guid replacesThroughMessageId,
        string? providerKind,
        string? modelId,
        Usage? usage,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var blocks = new ChatContentBlock[] { new ChatContentBlock.TextBlock(summaryText) };
        var entity = new AgentMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            ParentMessageId = null,
            Role = "assistant",
            Kind = "summary",
            ReplacesThroughMessageId = replacesThroughMessageId,
            ContentJson = JsonSerializer.Serialize(SerializeBlocks(blocks)),
            ProviderKind = providerKind,
            ModelId = modelId,
            InputTokens = usage?.InputTokens,
            OutputTokens = usage?.OutputTokens,
            CacheReadTokens = usage?.CacheReadTokens,
            CacheWriteTokens = usage?.CacheWriteTokens,
            StopReason = null,
            CreatedAtUtc = now
        };
        dbContext.AgentMessages.Add(entity);

        var convo = await dbContext.AgentConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (convo is not null)
        {
            convo.UpdatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    private static ChatRole ParseRole(string role) => role switch
    {
        "user" => ChatRole.User,
        "assistant" => ChatRole.Assistant,
        "tool" => ChatRole.Tool,
        "system" => ChatRole.System,
        _ => ChatRole.User
    };

    private static IReadOnlyList<object> SerializeBlocks(IReadOnlyList<ChatContentBlock> blocks)
    {
        var result = new List<object>(blocks.Count);
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ChatContentBlock.TextBlock t:
                    result.Add(new { type = "text", text = t.Text });
                    break;
                case ChatContentBlock.ToolUseBlock tu:
                    result.Add(new
                    {
                        type = "tool_use",
                        toolUseId = tu.ToolUseId,
                        name = tu.Name,
                        args = tu.Args
                    });
                    break;
                case ChatContentBlock.ToolResultBlock tr:
                    result.Add(new
                    {
                        type = "tool_result",
                        toolUseId = tr.ToolUseId,
                        result = tr.Result,
                        isError = tr.IsError
                    });
                    break;
            }
        }
        return result;
    }

    private static IReadOnlyList<ChatContentBlock> DeserializeBlocks(string contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return Array.Empty<ChatContentBlock>();
        using var doc = JsonDocument.Parse(contentJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<ChatContentBlock>();

        var result = new List<ChatContentBlock>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String) continue;
            switch (typeProp.GetString())
            {
                case "text":
                    if (element.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        result.Add(new ChatContentBlock.TextBlock(text.GetString() ?? string.Empty));
                    }
                    break;
                case "tool_use":
                    if (element.TryGetProperty("toolUseId", out var tuId) && element.TryGetProperty("name", out var tuName))
                    {
                        var args = element.TryGetProperty("args", out var a) ? a.Clone() : EmptyObject();
                        result.Add(new ChatContentBlock.ToolUseBlock(tuId.GetString() ?? string.Empty, tuName.GetString() ?? string.Empty, args));
                    }
                    break;
                case "tool_result":
                    if (element.TryGetProperty("toolUseId", out var trId))
                    {
                        var resultEl = element.TryGetProperty("result", out var r) ? r.Clone() : EmptyObject();
                        var isError = element.TryGetProperty("isError", out var ie) && ie.ValueKind == JsonValueKind.True;
                        result.Add(new ChatContentBlock.ToolResultBlock(trId.GetString() ?? string.Empty, resultEl, isError));
                    }
                    break;
            }
        }
        return result;
    }

    private static JsonElement EmptyObject()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    private static AgentConversationDto ToConversationDto(AgentConversation entity) => new(
        Id: entity.Id,
        UserId: entity.UserId,
        PageKey: entity.PageKey,
        Title: entity.Title,
        ProviderKind: entity.ProviderKind,
        ModelId: entity.ModelId,
        ConnectionId: entity.ConnectionId,
        CreatedAtUtc: entity.CreatedAtUtc,
        UpdatedAtUtc: entity.UpdatedAtUtc,
        LastMessageAtUtc: entity.LastMessageAtUtc);

    private static AgentMessageDto ToMessageDto(AgentMessage entity)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(entity.ContentJson) ? "[]" : entity.ContentJson);
        return new AgentMessageDto(
            Id: entity.Id,
            Role: entity.Role,
            Content: doc.RootElement.Clone(),
            ProviderKind: entity.ProviderKind,
            ModelId: entity.ModelId,
            InputTokens: entity.InputTokens,
            OutputTokens: entity.OutputTokens,
            StopReason: entity.StopReason,
            CreatedAtUtc: entity.CreatedAtUtc);
    }

    private static AgentToolCallDto ToToolCallDto(AgentToolCall entity)
    {
        using var argsDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(entity.ArgsJson) ? "{}" : entity.ArgsJson);
        JsonElement? result = null;
        if (!string.IsNullOrWhiteSpace(entity.ResultJson))
        {
            using var doc = JsonDocument.Parse(entity.ResultJson);
            result = doc.RootElement.Clone();
        }
        return new AgentToolCallDto(
            Id: entity.Id,
            MessageId: entity.MessageId,
            ToolUseId: entity.ToolUseId,
            ToolName: entity.ToolName,
            Args: argsDoc.RootElement.Clone(),
            Result: result,
            Status: entity.Status,
            ErrorText: entity.ErrorText,
            StartedAtUtc: entity.StartedAtUtc,
            FinishedAtUtc: entity.FinishedAtUtc,
            DurationMs: entity.DurationMs);
    }
}
