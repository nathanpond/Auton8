import { useCallback, useRef, useState } from "react";
import { pageActionResultsUrl, pageQueryResultsUrl, sendMessageUrl } from "./api";
import { AgentStreamEvent } from "./types";
import { PageActionResult, PageQueryResult, PageSnapshot } from "./pageContext/types";

export type AgentStreamState = {
  streaming: boolean;
  text: string; // accumulated assistant text for the current turn
  toolCalls: Array<{
    toolCallId: string;
    toolUseId: string;
    name: string;
    args: unknown;
    status: "running" | "succeeded" | "failed";
    result?: unknown;
    error?: string;
    durationMs?: number;
  }>;
  error: string | null;
  lastEvent: AgentStreamEvent | null;
};

const initial: AgentStreamState = {
  streaming: false,
  text: "",
  toolCalls: [],
  error: null,
  lastEvent: null
};

// Handler the chat sidebar passes when it sends a message. Invoked when the
// server streams a page_query_request SSE event mid-tool-call. The handler
// asks the registered page provider for the data and POSTs the result back
// to the server, where it unblocks the awaiting skill.
export type PageQueryDispatcher = (
  request: { queryId: string; topic: string; args?: unknown }
) => Promise<PageQueryResult>;

export type PageActionDispatcher = (
  request: { actionId: string; action: string; args?: unknown }
) => Promise<PageActionResult>;

export function useAgentStream() {
  const [state, setState] = useState<AgentStreamState>(initial);
  const abortRef = useRef<AbortController | null>(null);

  const reset = useCallback(() => setState(initial), []);

  const cancel = useCallback(() => {
    abortRef.current?.abort();
  }, []);

  const send = useCallback(async (
    conversationId: string,
    text: string,
    options?: {
      pageContext?: PageSnapshot | null;
      onPageQuery?: PageQueryDispatcher;
      onPageAction?: PageActionDispatcher;
      onComplete?: () => void;
    }
  ): Promise<void> => {
    const controller = new AbortController();
    abortRef.current = controller;

    setState({ streaming: true, text: "", toolCalls: [], error: null, lastEvent: null });

    const body: { text: string; pageContext?: PageSnapshot } = { text };
    if (options?.pageContext) body.pageContext = options.pageContext;

    try {
      const res = await fetch(sendMessageUrl(conversationId), {
        method: "POST",
        body: JSON.stringify(body),
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        signal: controller.signal
      });

      if (!res.ok || !res.body) {
        setState((s) => ({ ...s, streaming: false, error: `HTTP ${res.status}` }));
        return;
      }

      const reader = res.body.getReader();
      const decoder = new TextDecoder();
      let buffer = "";

      for (;;) {
        const { value, done } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });

        let frameEnd: number;
        while ((frameEnd = buffer.indexOf("\n\n")) >= 0) {
          const frame = buffer.slice(0, frameEnd);
          buffer = buffer.slice(frameEnd + 2);
          if (!frame.startsWith("data: ")) continue;
          const json = frame.slice("data: ".length).trim();
          if (!json) continue;
          let event: AgentStreamEvent;
          try {
            event = JSON.parse(json);
          } catch {
            continue;
          }
          // Page-query and page-action requests are NOT applied to UI
          // state — they're routed to the page provider and replied to
          // out-of-band.
          if (event.kind === "page_query_request") {
            void handlePageQuery(conversationId, event, options?.onPageQuery, controller.signal);
            continue;
          }
          if (event.kind === "page_action_request") {
            void handlePageAction(conversationId, event, options?.onPageAction, controller.signal);
            continue;
          }
          setState((s) => applyEvent(s, event));
          if (event.kind === "done" || event.kind === "error") {
            break;
          }
        }
      }
    } catch (err) {
      const isAbort = (err as { name?: string })?.name === "AbortError";
      setState((s) => ({
        ...s,
        streaming: false,
        error: isAbort ? "Cancelled." : (err as Error).message ?? "Stream failed."
      }));
    } finally {
      setState((s) => ({ ...s, streaming: false }));
      options?.onComplete?.();
    }
  }, []);

  return { state, send, cancel, reset };
}

async function handlePageAction(
  conversationId: string,
  event: Extract<AgentStreamEvent, { kind: "page_action_request" }>,
  dispatcher: PageActionDispatcher | undefined,
  signal: AbortSignal
): Promise<void> {
  let result: PageActionResult;
  if (!dispatcher) {
    result = { ok: false, error: "page_unreachable", message: "No page provider registered." };
  } else {
    try {
      result = await dispatcher({ actionId: event.actionId, action: event.action, args: event.args });
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      result = { ok: false, error: "handler_threw", message };
    }
  }

  const wireResult = result.ok
    ? { ok: true, summary: result.summary, changes: result.changes ?? null }
    : { ok: false, error: result.error, message: result.message ?? null };

  try {
    await fetch(pageActionResultsUrl(conversationId), {
      method: "POST",
      body: JSON.stringify({ actionId: event.actionId, result: wireResult }),
      headers: { "Content-Type": "application/json" },
      credentials: "include",
      signal
    });
  } catch (err) {
    if ((err as { name?: string })?.name !== "AbortError") {
      console.warn("page-action: failed to deliver result", err);
    }
  }
}

async function handlePageQuery(
  conversationId: string,
  event: Extract<AgentStreamEvent, { kind: "page_query_request" }>,
  dispatcher: PageQueryDispatcher | undefined,
  signal: AbortSignal
): Promise<void> {
  let result: PageQueryResult;
  if (!dispatcher) {
    result = { ok: false, error: "page_unreachable", message: "No page provider registered." };
  } else {
    try {
      result = await dispatcher({ queryId: event.queryId, topic: event.topic, args: event.args });
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      result = { ok: false, error: "handler_threw", message };
    }
  }

  // Map the SPA-internal result shape to the wire-format the server expects.
  // (server expects: { ok, data?, error?, message? })
  const wireResult = result.ok
    ? { ok: true, data: result.data ?? null }
    : { ok: false, error: result.error, message: result.message ?? null };

  try {
    await fetch(pageQueryResultsUrl(conversationId), {
      method: "POST",
      body: JSON.stringify({ queryId: event.queryId, result: wireResult }),
      headers: { "Content-Type": "application/json" },
      credentials: "include",
      signal
    });
  } catch (err) {
    if ((err as { name?: string })?.name !== "AbortError") {
      console.warn("page-query: failed to deliver result", err);
    }
  }
}

function applyEvent(state: AgentStreamState, event: AgentStreamEvent): AgentStreamState {
  switch (event.kind) {
    case "text_delta":
      return { ...state, text: state.text + event.delta, lastEvent: event };
    case "tool_started":
      return {
        ...state,
        lastEvent: event,
        toolCalls: [
          ...state.toolCalls,
          {
            toolCallId: event.toolCallId,
            toolUseId: event.toolUseId,
            name: event.name,
            args: event.args,
            status: "running"
          }
        ]
      };
    case "tool_completed":
      return {
        ...state,
        lastEvent: event,
        toolCalls: state.toolCalls.map((tc) =>
          tc.toolCallId === event.toolCallId
            ? { ...tc, status: "succeeded", result: event.result, durationMs: event.durationMs }
            : tc
        )
      };
    case "tool_failed":
      return {
        ...state,
        lastEvent: event,
        toolCalls: state.toolCalls.map((tc) =>
          tc.toolCallId === event.toolCallId
            ? { ...tc, status: "failed", error: event.error, durationMs: event.durationMs }
            : tc
        )
      };
    case "error":
      return { ...state, lastEvent: event, error: event.message };
    case "message_started":
    case "message_completed":
    case "done":
      return { ...state, lastEvent: event };
    default:
      return state;
  }
}
