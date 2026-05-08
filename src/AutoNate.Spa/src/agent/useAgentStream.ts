import { useCallback, useRef, useState } from "react";
import { sendMessageUrl } from "./api";
import { AgentStreamEvent } from "./types";

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
    onComplete?: () => void
  ): Promise<void> => {
    const controller = new AbortController();
    abortRef.current = controller;

    setState({ streaming: true, text: "", toolCalls: [], error: null, lastEvent: null });

    try {
      const res = await fetch(sendMessageUrl(conversationId), {
        method: "POST",
        body: JSON.stringify({ text }),
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
      onComplete?.();
    }
  }, []);

  return { state, send, cancel, reset };
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
