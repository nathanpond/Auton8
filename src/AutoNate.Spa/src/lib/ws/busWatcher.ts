export type WorkflowEventBusMessage = {
  receivedAtUtc: string;
  topic: string;
  contentType?: string | null;
  headers: Record<string, string>;
  payload: string;
};

export type BusConnectionStatus =
  | "Connecting..."
  | "Connected"
  | "Reconnecting..."
  | "Connection error"
  | "Disconnected";

export type BusConnectionOptions = {
  onStatusChanged?: (status: BusConnectionStatus) => void;
  onMessage?: (message: string) => void;
  closeReason?: string;
  path?: string;
};

export type BusConnection = {
  dispose: () => void;
};

export function createBusConnection(options: BusConnectionOptions = {}): BusConnection {
  const path = options.path ?? "/ws/bus-watcher";
  const closeReason = options.closeReason ?? "Client disposed";

  let socket: WebSocket | null = null;
  let reconnectTimer: number | null = null;
  let disposed = false;

  const status = (next: BusConnectionStatus) => {
    options.onStatusChanged?.(next);
  };

  const connect = () => {
    if (disposed) {
      return;
    }

    status("Connecting...");

    const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
    socket = new WebSocket(`${protocol}//${window.location.host}${path}`);

    // Every handler guards on `disposed` so a delayed open/message/error
    // event from an already-cancelled socket can't drive the consumer's
    // status callback or push data into an unmounted component.
    socket.addEventListener("open", () => {
      if (disposed) return;
      status("Connected");
    });

    socket.addEventListener("message", (event) => {
      if (disposed) return;
      const data = typeof event.data === "string" ? event.data : String(event.data);
      options.onMessage?.(data);
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
        socket.close(1000, closeReason);
      }

      socket = null;
    }
  };
}
