using System.Text.Json;
using AutoNate.Web.Services.Agent.Conversations;

namespace AutoNate.Web.Services.Agent.Loop;

public interface IAgentSession
{
    Task<AgentConversationDto> StartAsync(
        Guid userId,
        string pageKey,
        Guid? connectionId,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentEvent> SendMessageAsync(
        Guid conversationId,
        Guid userId,
        string userText,
        PageContextInput? pageContext = null,
        CancellationToken cancellationToken = default);
}

// Validated, server-side view of the page snapshot the SPA sent with the
// user message. Endpoints map their wire DTO to this; tests can construct
// it directly. Mirrors the SPA's PageSnapshot shape one-to-one.
public sealed record class PageContextInput(
    string PageKey,
    int SchemaVersion,
    string? Summary,
    long Version,
    JsonElement Data);
