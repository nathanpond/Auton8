import { FormEvent, useEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AgentConversation,
  AgentConversationDetail,
  AgentMessage,
  AgentMessageContentBlock
} from "./types";
import {
  createConversation,
  deleteConversation,
  getConversation,
  listConversations
} from "./api";
import { usePageKey } from "./usePageKey";
import { useAgentStream } from "./useAgentStream";
import { useAgentSidebar } from "./AgentSidebarContext";
import { useUserPreferences } from "@/preferences/UserPreferencesContext";
import { MarkdownView } from "./MarkdownView";
import { useActivePageSummary, usePageContextRegistry } from "./pageContext/PageContextRegistry";
import "./AgentSidebar.css";

export function AgentSidebar() {
  const pageKey = usePageKey();
  const { isOpen, close } = useAgentSidebar();
  const { chatbotWindowMode, chatbotOverHeader } = useUserPreferences();
  const pageContextRegistry = usePageContextRegistry();
  const activePageSummary = useActivePageSummary(pageKey);

  // Toggle a body class for "fill" mode so the layout can push #app's
  // children left to make room for the sidebar. Fixed-position sidebar stays
  // pinned to the viewport regardless. Cleared on close so closed sidebar
  // never reserves space.
  useEffect(() => {
    if (typeof document === "undefined") return;
    const body = document.body;
    const shouldFill = isOpen && chatbotWindowMode === "fill";
    body.classList.toggle("agent-sidebar-fill", shouldFill);
    return () => {
      body.classList.remove("agent-sidebar-fill");
    };
  }, [isOpen, chatbotWindowMode]);
  const [conversationId, setConversationId] = useState<string | null>(null);
  const queryClient = useQueryClient();

  // When the page key changes, drop the active conversation so the user sees
  // the conversation list scoped to where they are now.
  useEffect(() => {
    setConversationId(null);
  }, [pageKey]);

  const listKey = useMemo(() => ["agent", "conversations", pageKey] as const, [pageKey]);
  const detailKey = useMemo(() => ["agent", "conversation", conversationId] as const, [conversationId]);

  const conversationsQuery = useQuery({
    queryKey: listKey,
    queryFn: ({ signal }) => listConversations(pageKey, signal),
    enabled: isOpen
  });

  const detailQuery = useQuery<AgentConversationDetail>({
    queryKey: detailKey,
    queryFn: ({ signal }) => getConversation(conversationId!, signal),
    enabled: isOpen && !!conversationId
  });

  const createMutation = useMutation({
    mutationFn: () => createConversation(pageKey),
    onSuccess: (created) => {
      queryClient.invalidateQueries({ queryKey: ["agent", "conversations"] });
      setConversationId(created.id);
    }
  });

  const deleteMutation = useMutation({
    mutationFn: deleteConversation,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["agent", "conversations"] });
      setConversationId(null);
    }
  });

  const stream = useAgentStream();
  const [composer, setComposer] = useState("");
  // The just-sent user text. Rendered as a user bubble below the persisted
  // history while the assistant streams its reply, then cleared once the
  // refetch lands the persisted user message — no flicker because the
  // persisted bubble is there before this one disappears.
  const [pendingUserText, setPendingUserText] = useState<string | null>(null);

  const composerRef = useRef<HTMLTextAreaElement>(null);
  // Tracks the previous streaming flag so we focus the composer specifically
  // on the true→false transition (a reply just finished), not on initial
  // mount or any other rerender that finds streaming = false.
  const wasStreamingRef = useRef(false);
  useEffect(() => {
    if (wasStreamingRef.current && !stream.state.streaming) {
      composerRef.current?.focus();
    }
    wasStreamingRef.current = stream.state.streaming;
  }, [stream.state.streaming]);

  const onSubmit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    let id = conversationId;
    if (!id) {
      const created = await createMutation.mutateAsync();
      id = created.id;
    }
    if (!id) return;
    const text = composer;
    if (text.trim().length === 0) return;
    setComposer("");
    setPendingUserText(text);
    try {
      const pageContext = pageContextRegistry.getActiveSnapshot(pageKey);
      await stream.send(id, text, {
        pageContext,
        onPageQuery: (req) => pageContextRegistry.dispatchPageQuery(pageKey, req)
      });
      // Refetch AFTER the stream finishes so the persisted user + assistant
      // messages appear together; await so the "clear pending" line below
      // only runs once the persisted copy is in the cache.
      await queryClient.invalidateQueries({ queryKey: ["agent", "conversation", id] });
      await queryClient.invalidateQueries({ queryKey: ["agent", "conversations"] });
    } finally {
      setPendingUserText(null);
    }
  };

  return (
    <aside
      className={[
        "agent-sidebar",
        isOpen ? "agent-sidebar--open" : "",
        `agent-sidebar--mode-${chatbotWindowMode}`,
        chatbotOverHeader ? "agent-sidebar--over-header" : "agent-sidebar--under-header"
      ]
        .filter(Boolean)
        .join(" ")}
      aria-hidden={!isOpen}
    >
      {isOpen && (
        <div className="agent-sidebar__inner">
          <header className="agent-sidebar__header">
            <div className="fw-semibold">AutoNate Assistant</div>
            <small className="text-muted" title={activePageSummary ?? undefined}>
              {activePageSummary
                ? `page: ${pageKey} · ${truncate(activePageSummary, 60)}`
                : `page: ${pageKey}`}
            </small>
            <button
              type="button"
              className="btn btn-sm btn-link ms-auto"
              onClick={() => createMutation.mutate()}
              disabled={createMutation.isPending}
            >
              <i className="fa fa-plus me-1" /> New chat
            </button>
            <button
              type="button"
              className="btn btn-sm btn-icon"
              onClick={close}
              aria-label="Close assistant"
              title="Close"
            >
              <i className="fa fa-times" />
            </button>
          </header>

            <div className="agent-sidebar__body">
              {!conversationId && (
                <ConversationList
                  conversations={conversationsQuery.data ?? []}
                  loading={conversationsQuery.isLoading}
                  onSelect={(id) => setConversationId(id)}
                  onDelete={(id) => deleteMutation.mutate(id)}
                />
              )}

              {conversationId && (
                <ChatThread
                  conversationId={conversationId}
                  detail={detailQuery.data}
                  loading={detailQuery.isLoading}
                  streamText={stream.state.text}
                  streaming={stream.state.streaming}
                  toolCalls={stream.state.toolCalls}
                  errorText={stream.state.error}
                  pendingUserText={pendingUserText}
                  onBack={() => setConversationId(null)}
                  onDelete={() => deleteMutation.mutate(conversationId)}
                />
              )}
            </div>

            <form className="agent-sidebar__composer" onSubmit={onSubmit}>
              <textarea
                ref={composerRef}
                className="form-control"
                rows={2}
                value={composer}
                onChange={(e) => setComposer(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter" && !e.shiftKey) {
                    e.preventDefault();
                    if (composer.trim().length > 0) {
                      void onSubmit(e as unknown as FormEvent<HTMLFormElement>);
                    }
                  }
                }}
                placeholder={conversationId ? "Send a message…" : "Ask the assistant about this page…"}
                disabled={stream.state.streaming}
              />
              <div className="d-flex justify-content-end gap-2 mt-2">
                {stream.state.streaming ? (
                  <button type="button" className="btn btn-sm btn-outline-danger" onClick={stream.cancel}>
                    Stop
                  </button>
                ) : (
                  <button type="submit" className="btn btn-sm btn-primary" disabled={composer.trim().length === 0}>
                    Send
                  </button>
                )}
              </div>
            </form>
        </div>
      )}
    </aside>
  );
}

type ConversationListProps = {
  conversations: AgentConversation[];
  loading: boolean;
  onSelect: (id: string) => void;
  onDelete: (id: string) => void;
};

function ConversationList({ conversations, loading, onSelect, onDelete }: ConversationListProps) {
  if (loading) return <div className="p-3 text-muted">Loading…</div>;
  if (conversations.length === 0) {
    return (
      <div className="p-3 text-muted">
        No chats on this page yet. Send a message below to start a new one.
      </div>
    );
  }
  return (
    <ul className="list-group list-group-flush agent-conversations">
      {conversations.map((c) => (
        <li key={c.id} className="list-group-item d-flex align-items-start">
          <button
            type="button"
            className="btn btn-link text-start flex-grow-1 p-0"
            onClick={() => onSelect(c.id)}
          >
            <div className="fw-semibold">{c.title ?? "Untitled chat"}</div>
            <small className="text-muted">
              {c.lastMessageAtUtc ? new Date(c.lastMessageAtUtc).toLocaleString() : "(empty)"}
            </small>
          </button>
          <button
            type="button"
            className="btn btn-sm btn-link text-danger"
            aria-label={`Delete ${c.title ?? "chat"}`}
            onClick={() => {
              if (window.confirm("Delete this chat permanently?")) onDelete(c.id);
            }}
          >
            <i className="fa fa-trash" />
          </button>
        </li>
      ))}
    </ul>
  );
}

type ChatThreadProps = {
  conversationId: string;
  detail: AgentConversationDetail | undefined;
  loading: boolean;
  streamText: string;
  streaming: boolean;
  toolCalls: ReturnType<typeof useAgentStream>["state"]["toolCalls"];
  errorText: string | null;
  pendingUserText: string | null;
  onBack: () => void;
  onDelete: () => void;
};

function ChatThread({ detail, loading, streamText, streaming, toolCalls, errorText, pendingUserText, onBack, onDelete }: ChatThreadProps) {
  const messagesRef = useRef<HTMLDivElement>(null);

  // Pin the scroll viewport to the bottom whenever new content lands — a new
  // persisted message from either side, the user's just-sent pending bubble,
  // or streaming tokens / tool-call cards from the assistant. Unconditional
  // by request: always follow the latest activity.
  useEffect(() => {
    const el = messagesRef.current;
    if (!el) return;
    el.scrollTop = el.scrollHeight;
  }, [detail?.messages.length, pendingUserText, streamText, toolCalls.length, streaming]);

  if (loading || !detail) return <div className="p-3 text-muted">Loading…</div>;

  return (
    <div className="agent-thread">
      <div className="d-flex align-items-center px-3 py-2 border-bottom">
        <button type="button" className="btn btn-sm btn-link" onClick={onBack}>
          <i className="fa fa-arrow-left" /> Chats
        </button>
        <div className="flex-grow-1" />
        <button
          type="button"
          className="btn btn-sm btn-link text-danger"
          onClick={() => {
            if (window.confirm("Delete this chat permanently?")) onDelete();
          }}
        >
          <i className="fa fa-trash" /> Delete
        </button>
      </div>
      <div className="agent-thread__messages" ref={messagesRef}>
        {detail.messages.map((m) => (
          <MessageBubble key={m.id} message={m} toolCallsForMessage={detail.toolCalls.filter((tc) => tc.messageId === m.id)} />
        ))}
        {pendingUserText && (
          <div className="agent-bubble agent-bubble--user">{pendingUserText}</div>
        )}
        {streaming && (
          <div className="agent-bubble agent-bubble--assistant">
            {toolCalls.map((tc) => (
              <ToolCallCard
                key={tc.toolCallId}
                name={tc.name}
                args={tc.args}
                status={tc.status === "running" ? "pending" : tc.status}
                result={tc.result}
                error={tc.error}
                durationMs={tc.durationMs}
              />
            ))}
            {streamText && <MarkdownView source={streamText} />}
            {!streamText && toolCalls.length === 0 && <TypingIndicator />}
          </div>
        )}
        {errorText && <div className="alert alert-danger m-2">{errorText}</div>}
      </div>
    </div>
  );
}

type MessageBubbleProps = {
  message: AgentMessage;
  toolCallsForMessage: AgentConversationDetail["toolCalls"];
};

function MessageBubble({ message, toolCallsForMessage }: MessageBubbleProps) {
  // Skip the synthetic "tool" messages — they're already rendered as cards
  // beneath the assistant turn that requested them.
  if (message.role === "tool") return null;

  const text = message.content
    .filter((b): b is Extract<AgentMessageContentBlock, { type: "text" }> => b.type === "text")
    .map((b) => b.text)
    .join("\n");

  return (
    <div className={`agent-bubble agent-bubble--${message.role}`}>
      {toolCallsForMessage.map((tc) => (
        <ToolCallCard
          key={tc.id}
          name={tc.toolName}
          args={tc.args}
          status={tc.status as "pending" | "succeeded" | "failed"}
          result={tc.result}
          error={tc.errorText}
          durationMs={tc.durationMs ?? undefined}
        />
      ))}
      {text && (message.role === "assistant"
        ? <MarkdownView source={text} />
        : <div>{text}</div>)}
    </div>
  );
}

function truncate(value: string, max: number): string {
  if (value.length <= max) return value;
  return value.slice(0, max - 1) + "…";
}

function TypingIndicator() {
  return (
    <div className="agent-typing" aria-label="Assistant is typing">
      <span className="agent-typing__dot" />
      <span className="agent-typing__dot" />
      <span className="agent-typing__dot" />
    </div>
  );
}

type ToolCallCardProps = {
  name: string;
  args: unknown;
  status: "pending" | "succeeded" | "failed";
  result?: unknown;
  error?: string | null;
  durationMs?: number;
};

function ToolCallCard({ name, args, status, result, error, durationMs }: ToolCallCardProps) {
  const [open, setOpen] = useState(false);
  const badge =
    status === "pending" ? "bg-secondary" : status === "succeeded" ? "bg-success" : "bg-danger";
  return (
    <div className="agent-tool-call">
      <button type="button" className="agent-tool-call__head" onClick={() => setOpen((o) => !o)}>
        <span className={`badge ${badge} me-2`}>{status}</span>
        <code>{name}</code>
        {typeof durationMs === "number" && <small className="text-muted ms-2">{durationMs}ms</small>}
      </button>
      {open && (
        <div className="agent-tool-call__body">
          <div>
            <strong>args:</strong>
            <pre>{JSON.stringify(args, null, 2)}</pre>
          </div>
          {result !== undefined && result !== null && (
            <div>
              <strong>result:</strong>
              <pre>{JSON.stringify(result, null, 2)}</pre>
            </div>
          )}
          {error && (
            <div className="text-danger">
              <strong>error:</strong> {error}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
