// WebSocket subscription to /ws/agent-model-default. The server sends a
// snapshot on connect and a new snapshot whenever an admin changes the
// default model on the Site Configuration > Chatbot > Models page. The
// chatbot footer reads the latest snapshot to render its "Model in use"
// label without polling and without manual refreshes.

export type AgentModelDefaultMessage = {
  modelId: string | null;
  displayName: string | null;
  provider: string | null;
};

export type AgentModelDefaultStatus =
  | "Connecting..."
  | "Connected"
  | "Reconnecting..."
  | "Connection error"
  | "Disconnected";

export type AgentModelDefaultOptions = {
  onMessage?: (message: AgentModelDefaultMessage) => void;
  onStatusChanged?: (status: AgentModelDefaultStatus) => void;
};

export type AgentModelDefaultConnection = {
  dispose: () => void;
};

export function createAgentModelDefaultConnection(
  options: AgentModelDefaultOptions = {}
): AgentModelDefaultConnection {
  const path = "/ws/agent-model-default";

  let socket: WebSocket | null = null;
  let reconnectTimer: number | null = null;
  let disposed = false;

  const status = (next: AgentModelDefaultStatus) => options.onStatusChanged?.(next);

  const connect = () => {
    if (disposed) return;
    status("Connecting...");

    const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
    socket = new WebSocket(`${protocol}//${window.location.host}${path}`);

    socket.addEventListener("open", () => {
      if (disposed) return;
      status("Connected");
    });

    socket.addEventListener("message", (event) => {
      if (disposed) return;
      const raw = typeof event.data === "string" ? event.data : String(event.data);
      try {
        const parsed = JSON.parse(raw) as AgentModelDefaultMessage;
        options.onMessage?.(parsed);
      } catch {
        // Server only sends well-formed JSON; if a frame slips through
        // malformed (proxy injection, mismatched protocol), ignore it
        // rather than crashing the consumer.
      }
    });

    socket.addEventListener("close", () => {
      if (disposed) return;
      status("Reconnecting...");
      reconnectTimer = window.setTimeout(connect, 2000);
    });

    socket.addEventListener("error", () => {
      if (disposed) return;
      status("Connection error");
    });
  };

  connect();

  return {
    dispose() {
      disposed = true;
      if (reconnectTimer !== null) {
        window.clearTimeout(reconnectTimer);
        reconnectTimer = null;
      }
      if (
        socket &&
        (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)
      ) {
        socket.close(1000, "Client disposed");
      }
      socket = null;
    }
  };
}
