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
        CancellationToken cancellationToken = default);
}
