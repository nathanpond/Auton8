import { FormEvent, useEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Button,
  Code,
  Group,
  Stack,
  Text,
  Textarea
} from "@mantine/core";
import { modals } from "@mantine/modals";
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
import { useAgentModelDefault } from "@/hooks/useAgentModelDefault";
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
  //
  // The push target depends on whether the chatbot is in "over header" mode:
  //  - over-header: push #app so the header itself narrows and its icons stay
  //    visible to the left of the chatbot (chatbot covers from y=0 down).
  //  - under-header: push only #content so the header stays full-width; if
  //    we also pushed the header here, the body bg would show through in the
  //    y=0..56 strip above the chatbot.
  useEffect(() => {
    if (typeof document === "undefined") return;
    const body = document.body;
    const shouldFill = isOpen && chatbotWindowMode === "fill";
    body.classList.toggle("agent-sidebar-fill-app", shouldFill && chatbotOverHeader);
    body.classList.toggle("agent-sidebar-fill-content", shouldFill && !chatbotOverHeader);
    return () => {
      body.classList.remove("agent-sidebar-fill-app");
      body.classList.remove("agent-sidebar-fill-content");
    };
  }, [isOpen, chatbotWindowMode, chatbotOverHeader]);
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
  const modelDefault = useAgentModelDefault();
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
        onPageQuery: (req) => pageContextRegistry.dispatchPageQuery(pageKey, req),
        onPageAction: (req) => pageContextRegistry.dispatchPageAction(pageKey, req)
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

  const confirmDeleteConversation = (id: string) => {
    modals.openConfirmModal({
      title: "Delete chat",
      children: <Text size="sm">Delete this chat permanently?</Text>,
      labels: { confirm: "Delete", cancel: "Cancel" },
      confirmProps: { color: "red" },
      onConfirm: () => deleteMutation.mutate(id)
    });
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
          <Box
            className="agent-sidebar__header"
            px="sm"
            py="xs"
            style={{ borderBottom: "1px solid var(--mantine-color-default-border)" }}
          >
            <Group gap="xs" wrap="nowrap" align="center">
              <Stack gap={0} style={{ flex: 1, minWidth: 0 }}>
                <Text fw={600} size="sm">
                  AutoNate Assistant
                </Text>
                <Text size="xs" c="dimmed" lineClamp={1} title={activePageSummary ?? undefined}>
                  {activePageSummary
                    ? `page: ${pageKey} · ${truncate(activePageSummary, 60)}`
                    : `page: ${pageKey}`}
                </Text>
              </Stack>
              <Button
                variant="subtle"
                size="compact-sm"
                leftSection={<i className="fa fa-plus" />}
                onClick={() => createMutation.mutate()}
                loading={createMutation.isPending}
              >
                New chat
              </Button>
              <ActionIcon
                variant="subtle"
                color="gray"
                onClick={close}
                aria-label="Close assistant"
                title="Close"
              >
                <i className="fa fa-times" />
              </ActionIcon>
            </Group>
          </Box>

          <div className="agent-sidebar__body">
            {!conversationId && (
              <ConversationList
                conversations={conversationsQuery.data ?? []}
                loading={conversationsQuery.isLoading}
                onSelect={(id) => setConversationId(id)}
                onDelete={confirmDeleteConversation}
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
                onDelete={() => confirmDeleteConversation(conversationId)}
              />
            )}
          </div>

          <Box
            component="form"
            className="agent-sidebar__composer"
            onSubmit={onSubmit}
            p="sm"
            style={{
              borderTop: "1px solid var(--mantine-color-default-border)",
              background: "var(--mantine-color-default-hover)"
            }}
          >
            <Textarea
              ref={composerRef}
              autosize
              minRows={2}
              maxRows={8}
              value={composer}
              onChange={(e) => setComposer(e.currentTarget.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter" && !e.shiftKey) {
                  e.preventDefault();
                  if (composer.trim().length > 0) {
                    void onSubmit(e as unknown as FormEvent<HTMLFormElement>);
                  }
                }
              }}
              placeholder={
                conversationId ? "Send a message…" : "Ask the assistant about this page…"
              }
              disabled={stream.state.streaming}
            />
            <Group justify="space-between" gap="xs" mt="xs" wrap="nowrap">
              <ModelInUseLabel current={modelDefault.current} status={modelDefault.status} />
              <Group gap="xs">
                {stream.state.streaming ? (
                  <Button size="xs" color="red" variant="outline" onClick={stream.cancel}>
                    Stop
                  </Button>
                ) : (
                  <Button size="xs" type="submit" disabled={composer.trim().length === 0}>
                    Send
                  </Button>
                )}
              </Group>
            </Group>
          </Box>
        </div>
      )}
    </aside>
  );
}

type ModelInUseLabelProps = {
  current: { modelId: string | null; displayName: string | null; provider: string | null } | null;
  status: string;
};

// Compact "Model: …" affordance pinned to the bottom-left of the
// chatbot's composer footer. Updates live whenever the admin promotes a
// new default in Site Configuration > Chatbot > Models — the websocket
// at /ws/agent-model-default pushes the new snapshot to every open
// chatbot tab and this component re-renders without a refresh.
function ModelInUseLabel({ current, status }: ModelInUseLabelProps) {
  let text: string;
  if (current && current.displayName) {
    text = current.displayName;
  } else if (current && current.modelId) {
    text = current.modelId;
  } else if (current) {
    text = "No default model";
  } else if (status === "Connecting...") {
    text = "Loading…";
  } else {
    text = status;
  }

  return (
    <Group gap={6} wrap="nowrap" style={{ minWidth: 0, maxWidth: "60%" }}>
      <i className="fa fa-microchip" aria-hidden style={{ opacity: 0.6 }} />
      <Text
        size="xs"
        c="dimmed"
        truncate
        title={
          current?.modelId
            ? `Model in use: ${current.modelId}${current.provider ? ` (${current.provider})` : ""}`
            : "Model in use"
        }
      >
        {text}
      </Text>
    </Group>
  );
}

type ConversationListProps = {
  conversations: AgentConversation[];
  loading: boolean;
  onSelect: (id: string) => void;
  onDelete: (id: string) => void;
};

function ConversationList({ conversations, loading, onSelect, onDelete }: ConversationListProps) {
  if (loading)
    return (
      <Text c="dimmed" p="md" size="sm">
        Loading…
      </Text>
    );
  if (conversations.length === 0) {
    return (
      <Text c="dimmed" p="md" size="sm">
        No chats on this page yet. Send a message below to start a new one.
      </Text>
    );
  }
  return (
    <Stack gap={0} className="agent-conversations">
      {conversations.map((c) => (
        <Group
          key={c.id}
          gap="xs"
          wrap="nowrap"
          align="flex-start"
          px="sm"
          py="xs"
          style={{ borderBottom: "1px solid var(--mantine-color-default-border)" }}
        >
          <Box
            component="button"
            type="button"
            onClick={() => onSelect(c.id)}
            style={{
              flex: 1,
              minWidth: 0,
              textAlign: "left",
              background: "transparent",
              border: 0,
              padding: 0,
              cursor: "pointer"
            }}
          >
            <Text fw={600} size="sm" truncate>
              {c.title ?? "Untitled chat"}
            </Text>
            <Text size="xs" c="dimmed">
              {c.lastMessageAtUtc ? new Date(c.lastMessageAtUtc).toLocaleString() : "(empty)"}
            </Text>
          </Box>
          <ActionIcon
            variant="subtle"
            color="red"
            size="sm"
            aria-label={`Delete ${c.title ?? "chat"}`}
            onClick={() => onDelete(c.id)}
          >
            <i className="fa fa-trash" />
          </ActionIcon>
        </Group>
      ))}
    </Stack>
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

function ChatThread({
  detail,
  loading,
  streamText,
  streaming,
  toolCalls,
  errorText,
  pendingUserText,
  onBack,
  onDelete
}: ChatThreadProps) {
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

  if (loading || !detail)
    return (
      <Text c="dimmed" p="md" size="sm">
        Loading…
      </Text>
    );

  return (
    <div className="agent-thread">
      <Group
        px="sm"
        py="xs"
        gap="xs"
        wrap="nowrap"
        style={{ borderBottom: "1px solid var(--mantine-color-default-border)" }}
      >
        <Button
          variant="subtle"
          size="compact-sm"
          leftSection={<i className="fa fa-arrow-left" />}
          onClick={onBack}
        >
          Chats
        </Button>
        <Box style={{ flex: 1 }} />
        <Button
          variant="subtle"
          color="red"
          size="compact-sm"
          leftSection={<i className="fa fa-trash" />}
          onClick={onDelete}
        >
          Delete
        </Button>
      </Group>
      <div className="agent-thread__messages" ref={messagesRef}>
        {detail.messages.map((m) => (
          <MessageBubble
            key={m.id}
            message={m}
            toolCallsForMessage={detail.toolCalls.filter((tc) => tc.messageId === m.id)}
          />
        ))}
        {pendingUserText && <div className="agent-bubble agent-bubble--user">{pendingUserText}</div>}
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
        {errorText && (
          <Alert color="red" variant="light" m="xs">
            {errorText}
          </Alert>
        )}
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
      {text &&
        (message.role === "assistant" ? <MarkdownView source={text} /> : <div>{text}</div>)}
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
  const badgeColor = status === "pending" ? "gray" : status === "succeeded" ? "green" : "red";
  return (
    <div className="agent-tool-call">
      <button
        type="button"
        className="agent-tool-call__head"
        onClick={() => setOpen((o) => !o)}
        style={{
          background: "transparent",
          border: 0,
          cursor: "pointer",
          width: "100%",
          textAlign: "left"
        }}
      >
        <Group gap="xs" wrap="nowrap">
          <Badge size="sm" color={badgeColor}>
            {status}
          </Badge>
          <Code>{name}</Code>
          {typeof durationMs === "number" && (
            <Text size="xs" c="dimmed">
              {durationMs}ms
            </Text>
          )}
        </Group>
      </button>
      {open && (
        <div className="agent-tool-call__body">
          <div>
            <Text fw={700} size="xs" component="strong">
              args:
            </Text>
            <pre>{JSON.stringify(args, null, 2)}</pre>
          </div>
          {result !== undefined && result !== null && (
            <div>
              <Text fw={700} size="xs" component="strong">
                result:
              </Text>
              <pre>{JSON.stringify(result, null, 2)}</pre>
            </div>
          )}
          {error && (
            <Text size="sm" c="red">
              <strong>error:</strong> {error}
            </Text>
          )}
        </div>
      )}
    </div>
  );
}
