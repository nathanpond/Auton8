import { useEffect, useRef, useState } from "react";
import {
  AgentModelDefaultMessage,
  AgentModelDefaultStatus,
  createAgentModelDefaultConnection
} from "@/lib/ws/agentModelDefault";

export type AgentModelDefaultState = {
  // Latest snapshot from the server. null until the first message
  // arrives (or if the websocket can't connect yet).
  current: AgentModelDefaultMessage | null;
  status: AgentModelDefaultStatus;
};

// Single-tab subscription to /ws/agent-model-default. The chatbot footer
// reads this to label the in-use model and react to admin-driven default
// changes without a refresh. The connection auto-reconnects, so the
// component using this hook doesn't need to retry manually.
export function useAgentModelDefault(): AgentModelDefaultState {
  const [current, setCurrent] = useState<AgentModelDefaultMessage | null>(null);
  const [status, setStatus] = useState<AgentModelDefaultStatus>("Connecting...");
  // Keep a ref to the latest setters so the connection callbacks bind once
  // and don't trigger reconnects on every render.
  const setCurrentRef = useRef(setCurrent);
  const setStatusRef = useRef(setStatus);
  setCurrentRef.current = setCurrent;
  setStatusRef.current = setStatus;

  useEffect(() => {
    const connection = createAgentModelDefaultConnection({
      onMessage: (msg) => setCurrentRef.current(msg),
      onStatusChanged: (s) => setStatusRef.current(s)
    });
    return () => connection.dispose();
  }, []);

  return { current, status };
}
