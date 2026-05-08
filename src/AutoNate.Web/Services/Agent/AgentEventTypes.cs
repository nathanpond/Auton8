namespace AutoNate.Web.Services.Agent;

// Topic + event-type names for the agent.events bus topic. An audit consumer
// can answer "what did each user ask the agent, what tools did it call, and
// did the model finish or error?" by reading this topic alone. No prompt
// content beyond a truncated text snippet appears in any payload — the full
// transcript lives in agent_message; the audit log links by id.
public static class AgentEventTopic
{
    public const string TopicRoot = "agent";
    public const string TopicName = "agent.events";
}

public static class AgentResourceKinds
{
    public const string Conversation = "agent-conversation";
    public const string Message = "agent-message";
    public const string ToolCall = "agent-tool-call";
}

public static class AgentEventTypes
{
    public const string ConversationCreated = "agent.conversation.created";
    public const string ConversationViewed = "agent.conversation.viewed";
    public const string ConversationListViewed = "agent.conversation.list_viewed";
    public const string ConversationRenamed = "agent.conversation.renamed";
    public const string ConversationDeleted = "agent.conversation.deleted";
    public const string MessageUserSent = "agent.message.user_sent";
    public const string MessageAssistantStarted = "agent.message.assistant_started";
    public const string MessageAssistantCompleted = "agent.message.assistant_completed";
    public const string MessageAssistantFailed = "agent.message.assistant_failed";
    public const string ToolInvoked = "agent.tool.invoked";
    public const string ToolCompleted = "agent.tool.completed";
    public const string ToolFailed = "agent.tool.failed";
}
