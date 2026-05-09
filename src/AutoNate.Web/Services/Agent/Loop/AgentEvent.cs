using System.Text.Json;
using AutoNate.Web.Services.Agent.Providers;

namespace AutoNate.Web.Services.Agent.Loop;

// Discriminated union for the SSE stream that the agent endpoint relays to
// the SPA. One AgentEvent is one `data: {...}` SSE frame.
public abstract record class AgentEvent
{
    public abstract string Kind { get; }

    public sealed record class MessageStarted(Guid MessageId) : AgentEvent
    {
        public override string Kind => "message_started";
    }

    public sealed record class TextDelta(string Delta) : AgentEvent
    {
        public override string Kind => "text_delta";
    }

    public sealed record class ToolStarted(Guid ToolCallId, string ToolUseId, string Name, JsonElement Args) : AgentEvent
    {
        public override string Kind => "tool_started";
    }

    public sealed record class ToolCompleted(Guid ToolCallId, string ToolUseId, JsonElement Result, long DurationMs) : AgentEvent
    {
        public override string Kind => "tool_completed";
    }

    public sealed record class ToolFailed(Guid ToolCallId, string ToolUseId, string ErrorMessage, long DurationMs) : AgentEvent
    {
        public override string Kind => "tool_failed";
    }

    public sealed record class MessageCompleted(Guid MessageId, ChatStopReason StopReason, Usage? Usage) : AgentEvent
    {
        public override string Kind => "message_completed";
    }

    // Server → SPA request to fetch a slice of live page state. Emitted by
    // IPageQueryChannel mid-tool-invocation when a skill needs data the
    // per-message snapshot doesn't carry. The SPA replies via
    // POST /api/agent/conversations/{id}/page-query-results.
    public sealed record class PageQueryRequested(string QueryId, string Topic, JsonElement? Args) : AgentEvent
    {
        public override string Kind => "page_query_request";
    }

    public sealed record class Error(string Message) : AgentEvent
    {
        public override string Kind => "error";
    }

    public sealed record class Done : AgentEvent
    {
        public override string Kind => "done";
    }
}
