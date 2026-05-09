export type AgentConversation = {
  id: string;
  userId: string;
  pageKey: string;
  title: string | null;
  providerKind: string | null;
  modelId: string | null;
  connectionId: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  lastMessageAtUtc: string | null;
};

export type AgentMessageContentBlock =
  | { type: "text"; text: string }
  | { type: "tool_use"; toolUseId: string; name: string; args: unknown }
  | { type: "tool_result"; toolUseId: string; result: unknown; isError: boolean };

export type AgentMessage = {
  id: string;
  role: "user" | "assistant" | "tool" | "system";
  content: AgentMessageContentBlock[];
  providerKind: string | null;
  modelId: string | null;
  inputTokens: number | null;
  outputTokens: number | null;
  stopReason: string | null;
  createdAtUtc: string;
};

export type AgentToolCall = {
  id: string;
  messageId: string;
  toolUseId: string;
  toolName: string;
  args: unknown;
  result: unknown | null;
  status: "pending" | "succeeded" | "failed" | "cancelled" | "denied";
  errorText: string | null;
  startedAtUtc: string;
  finishedAtUtc: string | null;
  durationMs: number | null;
};

export type AgentConversationDetail = {
  conversation: AgentConversation;
  messages: AgentMessage[];
  toolCalls: AgentToolCall[];
};

// Server-Sent Event payloads (one per SSE `data:` frame).
export type AgentStreamEvent =
  | { kind: "message_started"; messageId: string }
  | { kind: "text_delta"; delta: string }
  | { kind: "tool_started"; toolCallId: string; toolUseId: string; name: string; args: unknown }
  | { kind: "tool_completed"; toolCallId: string; toolUseId: string; result: unknown; durationMs: number }
  | { kind: "tool_failed"; toolCallId: string; toolUseId: string; error: string; durationMs: number }
  | {
      kind: "message_completed";
      messageId: string;
      stopReason: string;
      usage: {
        inputTokens: number;
        outputTokens: number;
        cacheReadTokens: number | null;
        cacheWriteTokens: number | null;
      } | null;
    }
  | { kind: "page_query_request"; queryId: string; topic: string; args?: unknown }
  | { kind: "error"; message: string }
  | { kind: "done" };
