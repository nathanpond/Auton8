import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Alert,
  Box,
  Button,
  Group,
  Loader,
  Stack,
  Text,
  Textarea,
  Tooltip
} from "@mantine/core";
import {
  createConversation,
  deleteConversation,
  getConversation,
  listConversations
} from "@/agent/api";
import { useAgentStream } from "@/agent/useAgentStream";
import { usePageContextRegistry } from "@/agent/pageContext/PageContextRegistry";
import { MarkdownView } from "@/agent/MarkdownView";
import type {
  AgentConversation,
  AgentConversationDetail,
  AgentMessage,
  AgentMessageContentBlock
} from "@/agent/types";
import { pageKeyForDocument } from "./useDocumentEditorPageContext";

// Phase 8 v1 — slim chat UI that mounts inside docx-editor's `agentPanel`
// render slot. Reuses the existing /api/agent/conversations/* infra
// (streaming SSE via useAgentStream) but scopes everything to a single
// per-document conversation thread.
//
// Per-document scoping uses pageKey = `document:UUID` so each open
// document gets its own conversation history independent of the global
// AgentSidebar (which uses the coarse "documents" pageKey). The
// matching PageContextProvider in useDocumentEditorPageContext registers
// against the same key so the chat snapshot has live access to the
// document's title + body preview + bindings catalog.
//
// v1 deliberately omits: conversation list (single thread per doc, auto-
// created on first send), model picker (server picks default), tool-call
// chrome (only renders text and tool-completion summaries), inline-assist
// integration (separate Phase 8b deliverable).

type Props = {
  documentId: string;
  onClose: () => void;
};

export default function DocumentChatPanel({ documentId, onClose }: Props) {
  const qc = useQueryClient();
  const pageKey = pageKeyForDocument(documentId);
  const registry = usePageContextRegistry();
  const { state: streamState, send, cancel } = useAgentStream();
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [composer, setComposer] = useState("");
  const messageEndRef = useRef<HTMLDivElement | null>(null);

  const listKey = useMemo(
    () => ["agent", "document-conversations", documentId] as const,
    [documentId]
  );
  const detailKey = useMemo(
    () => ["agent", "conversation", conversationId] as const,
    [conversationId]
  );

  // Fetch the document's conversations. v1 surfaces the most recent one;
  // a follow-up phase will add an in-panel list if users need history
  // navigation.
  const listQuery = useQuery({
    queryKey: listKey,
    queryFn: ({ signal }) => listConversations(pageKey, signal)
  });

  // When the list resolves, auto-select the most recent conversation
  // (preserved across reloads). If there are no prior conversations the
  // panel stays in "empty state" until the user clicks Send — we create
  // the conversation on first message, not on mount, so users opening
  // the panel to skim don't leave orphan empty threads behind.
  useEffect(() => {
    if (conversationId) return;
    const items = listQuery.data;
    if (items && items.length > 0) {
      setConversationId(items[0].id);
    }
  }, [conversationId, listQuery.data]);

  const detailQuery = useQuery<AgentConversationDetail>({
    queryKey: detailKey,
    queryFn: ({ signal }) => getConversation(conversationId!, signal),
    enabled: !!conversationId,
    // Don't burn CPU re-fetching a missing conversation — if it 404s,
    // the detect-stale effect below will drop the id and the panel
    // re-anchors to the next available conversation.
    retry: false
  });

  // Detect stale `conversationId`: if the detail query fails (typically
  // 404 because the conversation was deleted from another surface, or
  // the DB was wiped between dev sessions while the tab kept its React
  // state), clear it so the auto-select effect picks a fresh thread.
  // Without this, the panel header reads "Thread · N messages" but
  // every action against the id (delete, send) errors out.
  useEffect(() => {
    if (!conversationId) return;
    if (!detailQuery.isError) return;
    const status = (detailQuery.error as { response?: { status?: number } } | null)
      ?.response?.status;
    if (status === 404 || status === undefined) {
      setConversationId(null);
      qc.invalidateQueries({ queryKey: listKey });
    }
  }, [conversationId, detailQuery.isError, detailQuery.error, qc, listKey]);

  const createMutation = useMutation({
    mutationFn: () => createConversation(pageKey),
    onSuccess: (created) => {
      qc.invalidateQueries({ queryKey: listKey });
      setConversationId(created.id);
    }
  });

  const deleteMutation = useMutation({
    // Treat 404 as "already deleted" — no-op success rather than a hard
    // error. This shows up when the conversation was deleted from another
    // surface (e.g. the global AgentSidebar) or the DB was wiped between
    // dev runs. The intent (gone from the panel) is satisfied either way.
    mutationFn: async (id: string) => {
      try {
        await deleteConversation(id);
      } catch (err) {
        const status = (err as { response?: { status?: number } })?.response?.status;
        if (status !== 404) throw err;
      }
    },
    onSuccess: (_void, deletedId) => {
      // CRITICAL: synchronously evict the deleted conversation from the
      // list query's cache BEFORE clearing conversationId. Otherwise the
      // auto-select effect (which runs as soon as conversationId becomes
      // null) sees the still-stale cached list, picks items[0] — which
      // is the conversation we just deleted — and the panel snaps back
      // to a phantom thread until the eventual refetch completes. The
      // background refetch from invalidateQueries reconciles whatever
      // ordering quirks our cache write missed.
      qc.setQueryData<AgentConversation[]>(listKey, (old) =>
        old ? old.filter((c) => c.id !== deletedId) : old
      );
      qc.invalidateQueries({ queryKey: listKey });
      setConversationId(null);
    }
  });

  // Auto-scroll on every new message + every streaming delta. The ref
  // lands at the end of the message list, so scrolling it into view keeps
  // the most recent assistant text at the bottom edge of the panel.
  useEffect(() => {
    messageEndRef.current?.scrollIntoView({ block: "end" });
  }, [detailQuery.data?.messages.length, streamState.text]);

  const handleSend = async () => {
    const text = composer.trim();
    if (!text || streamState.streaming) return;

    let activeId = conversationId;
    if (!activeId) {
      // First message in the thread — spin up a conversation, then send
      // against it. createMutation invalidates listKey so the next render
      // already has the conversation; we still pass `activeId` into send()
      // for the SSE call.
      const created = await createMutation.mutateAsync();
      activeId = created.id;
    }
    setComposer("");

    const snapshot = registry.getActiveSnapshot(pageKey);
    // Capture `activeId` outside the onComplete closure so the
    // invalidation lands on the correct key even when this send
    // created the conversation (in which case the closed-over
    // `detailKey` was computed against conversationId=null).
    const targetDetailKey = ["agent", "conversation", activeId] as const;
    await send(activeId, text, {
      pageContext: snapshot,
      onPageQuery: (req) => registry.dispatchPageQuery(pageKey, req),
      onPageAction: (req) => registry.dispatchPageAction(pageKey, req),
      onComplete: () => {
        // Refresh the detail so the assistant message lands in the
        // persisted history (the stream is in-memory only).
        qc.invalidateQueries({ queryKey: targetDetailKey });
        qc.invalidateQueries({ queryKey: listKey });
      }
    });
  };

  const handleComposerKey = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    // Cmd/Ctrl+Enter = send. Plain Enter inserts a newline so multi-line
    // prompts don't fire prematurely.
    if ((e.metaKey || e.ctrlKey) && e.key === "Enter") {
      e.preventDefault();
      void handleSend();
    }
  };

  const handleNewThread = async () => {
    if (streamState.streaming) cancel();
    const created = await createMutation.mutateAsync();
    setConversationId(created.id);
  };

  const handleDeleteThread = async () => {
    if (!conversationId) return;
    if (streamState.streaming) cancel();
    try {
      await deleteMutation.mutateAsync(conversationId);
    } catch (err) {
      // The mutation already swallows 404. Anything else here is a
      // real server/network failure; log it but don't propagate as
      // an unhandled rejection — the user's intent (close the panel
      // thread) is best served by clearing local state regardless.
      console.warn("[chat] delete failed unexpectedly", err);
      setConversationId(null);
      qc.invalidateQueries({ queryKey: listKey });
    }
  };

  const messages = detailQuery.data?.messages ?? [];

  return (
    <Stack
      gap={0}
      style={{
        height: "100%",
        display: "flex",
        flexDirection: "column",
        minHeight: 0
      }}
    >
      {/* Header bar — thread actions only. The agentPanel chrome
          already renders a title + close button above us; this bar
          carries the new/delete affordances. */}
      <Group
        justify="space-between"
        align="center"
        px="sm"
        py={6}
        style={{
          borderBottom: "1px solid var(--mantine-color-gray-3)",
          flexShrink: 0
        }}
      >
        <Text size="xs" c="dimmed">
          {conversationId
            ? `Thread · ${messages.length} message${messages.length === 1 ? "" : "s"}`
            : "New conversation"}
        </Text>
        <Group gap={4}>
          <Tooltip label="Start a new thread" withArrow>
            <ActionIcon
              variant="subtle"
              size="sm"
              onClick={handleNewThread}
              aria-label="New thread"
              loading={createMutation.isPending}
            >
              <i className="fa fa-plus" aria-hidden />
            </ActionIcon>
          </Tooltip>
          {conversationId ? (
            <Tooltip label="Delete this thread" withArrow>
              <ActionIcon
                variant="subtle"
                color="red"
                size="sm"
                onClick={handleDeleteThread}
                aria-label="Delete thread"
                loading={deleteMutation.isPending}
              >
                <i className="fa fa-trash" aria-hidden />
              </ActionIcon>
            </Tooltip>
          ) : null}
        </Group>
      </Group>

      {/* Message list. Flex-1 so the composer stays pinned to the
          bottom; overflow-y-auto so long threads scroll independently. */}
      <Box
        style={{
          flex: 1,
          minHeight: 0,
          overflowY: "auto",
          padding: 12
        }}
      >
        {listQuery.isLoading ? (
          <Group justify="center" mt="sm">
            <Loader size="sm" />
          </Group>
        ) : messages.length === 0 && !streamState.streaming ? (
          <Stack gap={6} mt="sm">
            <Text size="sm" c="dimmed">
              Ask the assistant about this document. It sees the document's
              title, body, and live data bindings.
            </Text>
            <Text size="xs" c="dimmed">
              Examples: "summarize the introduction", "what record fields
              does this template reference?", "rewrite the second paragraph
              in a more formal tone".
            </Text>
          </Stack>
        ) : (
          <Stack gap="sm">
            {messages.map((m) => (
              <MessageRow key={m.id} message={m} />
            ))}
            {streamState.streaming || streamState.text ? (
              <StreamingMessageRow text={streamState.text} done={!streamState.streaming} />
            ) : null}
            <div ref={messageEndRef} />
          </Stack>
        )}
        {streamState.error ? (
          isContextOverflowError(streamState.error) ? (
            <Alert color="yellow" mt="sm" variant="light" title="Thread is full">
              <Text size="sm" mb={6}>
                This conversation has hit the model's context limit. Each
                document mutation keeps a full copy of the inserted markdown in
                the thread's history; after enough turns there's no room left
                for a new request.
              </Text>
              <Button
                size="xs"
                variant="filled"
                color="yellow"
                onClick={handleNewThread}
                leftSection={<i className="fa fa-plus" aria-hidden />}
              >
                Start a fresh thread
              </Button>
            </Alert>
          ) : (
            <Alert color="red" mt="sm" variant="light">
              {streamState.error}
            </Alert>
          )
        ) : null}
      </Box>

      {/* Composer. Single textarea + send button — no model picker or
          attachment widget in v1. The textarea auto-grows up to 6 rows
          via Mantine's autosize. */}
      <Stack
        gap={6}
        px="sm"
        py={8}
        style={{
          borderTop: "1px solid var(--mantine-color-gray-3)",
          flexShrink: 0
        }}
      >
        <Textarea
          value={composer}
          onChange={(e) => setComposer(e.currentTarget.value)}
          onKeyDown={handleComposerKey}
          placeholder="Ask about this document…"
          autosize
          minRows={2}
          maxRows={6}
          disabled={streamState.streaming}
        />
        <Group justify="space-between">
          <Text size="xs" c="dimmed">
            ⌘/Ctrl + Enter to send
          </Text>
          <Group gap="xs">
            {streamState.streaming ? (
              <Button size="xs" variant="default" onClick={cancel}>
                Stop
              </Button>
            ) : null}
            <Button
              size="xs"
              onClick={handleSend}
              disabled={!composer.trim() || streamState.streaming}
              loading={createMutation.isPending}
              leftSection={<i className="fa fa-paper-plane" aria-hidden />}
            >
              Send
            </Button>
          </Group>
        </Group>
      </Stack>
    </Stack>
  );
}

function MessageRow({ message }: { message: AgentMessage }) {
  // Render only user + assistant for the panel. Tool messages are
  // collapsed into their parent assistant turn via the streaming tool
  // chrome (skipped in v1 — just render the text blocks). System
  // messages don't show.
  if (message.role !== "user" && message.role !== "assistant") return null;
  const isUser = message.role === "user";
  const text = combineTextBlocks(message.content);
  if (!text) return null;
  return (
    <Box
      style={{
        display: "flex",
        justifyContent: isUser ? "flex-end" : "flex-start"
      }}
    >
      <Box
        style={{
          maxWidth: "85%",
          padding: "8px 12px",
          borderRadius: 8,
          background: isUser
            ? "var(--mantine-color-blue-light)"
            : "var(--mantine-color-gray-1)",
          color: "var(--mantine-color-black)",
          whiteSpace: "normal",
          wordBreak: "break-word"
        }}
      >
        {isUser ? (
          <Text size="sm" style={{ whiteSpace: "pre-wrap" }}>
            {text}
          </Text>
        ) : (
          <MarkdownView source={text} />
        )}
      </Box>
    </Box>
  );
}

function StreamingMessageRow({ text, done }: { text: string; done: boolean }) {
  return (
    <Box style={{ display: "flex", justifyContent: "flex-start" }}>
      <Box
        style={{
          maxWidth: "85%",
          padding: "8px 12px",
          borderRadius: 8,
          background: "var(--mantine-color-gray-1)"
        }}
      >
        {text ? <MarkdownView source={text} /> : null}
        {!done ? (
          <Text size="xs" c="dimmed" mt={4}>
            <Loader size="xs" type="dots" /> thinking…
          </Text>
        ) : null}
      </Box>
    </Box>
  );
}

// Recognize the family of "context window blown" errors so we can swap
// the raw provider 400 for an actionable message. Anthropic phrases it
// "prompt is too long: N tokens > 200000 maximum"; OpenAI uses
// "context_length_exceeded". Substring match, lower-cased — the wire
// text isn't part of either contract and could reword across releases.
function isContextOverflowError(text: string): boolean {
  const lower = text.toLowerCase();
  return (
    lower.includes("prompt is too long") ||
    lower.includes("context_length_exceeded") ||
    lower.includes("maximum context length")
  );
}

function combineTextBlocks(blocks: AgentMessageContentBlock[]): string {
  return blocks
    .filter((b): b is Extract<AgentMessageContentBlock, { type: "text" }> => b.type === "text")
    .map((b) => b.text)
    .join("\n")
    .trim();
}
