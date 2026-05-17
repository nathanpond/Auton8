// Scoped subscription client. Single websocket per tab, multiplexed across
// many channels via reference counting. Subscribes are sent on demand (or
// replayed on reconnect); per-channel rejections from the server are
// surfaced to handlers as `RejectionEvent`s so callers can stop polling.

export type ChannelEvent = {
  type: "event";
  channel: string;
  receivedAtUtc: string;
  topic: string;
  contentType?: string | null;
  headers: Record<string, string>;
  payload: string;
};

export type RejectionEvent = {
  type: "rejected";
  channel: string;
  code: string;
  reason?: string | null;
};

export type InvalidateEvent = {
  type: "invalidate";
  channel: string;
  reason?: string | null;
};

export type SubscriptionEvent = ChannelEvent | RejectionEvent | InvalidateEvent;

export type SubscriptionHandler = (event: SubscriptionEvent) => void;

export type SubscriptionStatus =
  | "Connecting..."
  | "Connected"
  | "Reconnecting..."
  | "Disconnected";

export type SubscriptionClient = {
  subscribe(channels: string[], handler: SubscriptionHandler): () => void;
  getStatus(): SubscriptionStatus;
  onStatusChange(listener: (status: SubscriptionStatus) => void): () => void;
};

// Same cap as the legacy connection — non-admin browsers (no live access)
// shouldn't hammer the endpoint forever.
const MAX_FAILED_CONNECT_ATTEMPTS = 3;
const RECONNECT_DELAY_MS = 2000;

type ServerFrame =
  | { type: "ack"; id?: string; subscribed?: string[]; unsubscribed?: string[]; rejected?: ServerRejection[] }
  | { type: "event"; channel: string; receivedAtUtc: string; topic: string; contentType?: string | null; headers: Record<string, string>; payload: string }
  | { type: "invalidate"; channels: string[]; reason?: string | null }
  | { type: "pong"; id?: string; ts?: number }
  | { type: "error"; code: string; reason?: string | null };

type ServerRejection = {
  channel: string;
  code: string;
  reason?: string | null;
};

let singleton: SubscriptionClient | null = null;

export function getSubscriptionClient(): SubscriptionClient {
  if (singleton) return singleton;
  singleton = createClient();
  return singleton;
}

function createClient(): SubscriptionClient {
  const path = "/ws/bus-watcher";

  // channel → handler set. Source of truth for "what should be subscribed."
  const handlers = new Map<string, Set<SubscriptionHandler>>();
  // Channels the server has acked as subscribed (clear on disconnect).
  const accepted = new Set<string>();
  // Channels the server has rejected this session — don't retry on reconnect.
  const rejected = new Set<string>();

  const statusListeners = new Set<(s: SubscriptionStatus) => void>();
  let status: SubscriptionStatus = "Disconnected";

  let socket: WebSocket | null = null;
  let reconnectTimer: number | null = null;
  let everOpened = false;
  let failedAttemptsSinceOpen = 0;
  let frameIdCounter = 0;

  const setStatus = (next: SubscriptionStatus) => {
    if (status === next) return;
    status = next;
    statusListeners.forEach((l) => l(next));
  };

  const nextFrameId = () => {
    frameIdCounter += 1;
    return `c-${frameIdCounter}`;
  };

  const fanOut = (channel: string, event: SubscriptionEvent) => {
    const set = handlers.get(channel);
    if (!set) return;
    set.forEach((h) => {
      try {
        h(event);
      } catch (err) {
        // Handler errors must not break the dispatch loop.
        console.error("Bus subscription handler threw:", err);
      }
    });
  };

  const sendIfOpen = (frame: object) => {
    if (!socket || socket.readyState !== WebSocket.OPEN) return;
    try {
      socket.send(JSON.stringify(frame));
    } catch {
      // Send failures will surface via the close handler.
    }
  };

  const sendSubscribe = (channels: string[]) => {
    if (channels.length === 0) return;
    sendIfOpen({ type: "subscribe", id: nextFrameId(), channels });
  };

  const sendUnsubscribe = (channels: string[]) => {
    if (channels.length === 0) return;
    sendIfOpen({ type: "unsubscribe", id: nextFrameId(), channels });
  };

  const handleServerFrame = (data: string) => {
    let frame: ServerFrame;
    try {
      frame = JSON.parse(data) as ServerFrame;
    } catch {
      return;
    }

    switch (frame.type) {
      case "ack":
        frame.subscribed?.forEach((c) => accepted.add(c));
        frame.rejected?.forEach((r) => {
          rejected.add(r.channel);
          accepted.delete(r.channel);
          fanOut(r.channel, {
            type: "rejected",
            channel: r.channel,
            code: r.code,
            reason: r.reason ?? null,
          });
        });
        frame.unsubscribed?.forEach((c) => accepted.delete(c));
        return;
      case "event":
        fanOut(frame.channel, {
          type: "event",
          channel: frame.channel,
          receivedAtUtc: frame.receivedAtUtc,
          topic: frame.topic,
          contentType: frame.contentType ?? null,
          headers: frame.headers ?? {},
          payload: frame.payload,
        });
        return;
      case "invalidate":
        frame.channels.forEach((c) => {
          fanOut(c, { type: "invalidate", channel: c, reason: frame.reason ?? null });
        });
        return;
      case "pong":
      case "error":
        // No-op for now; ping/pong is for liveness once the server adds idle
        // timeouts. Error frames are protocol-level and usually paired with a
        // close — they get logged for debugging.
        if (frame.type === "error") {
          console.warn("Bus subscription error:", frame);
        }
        return;
    }
  };

  const connect = () => {
    if (reconnectTimer !== null) {
      window.clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }

    setStatus("Connecting...");

    const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
    socket = new WebSocket(`${protocol}//${window.location.host}${path}`);
    let openedThisAttempt = false;

    socket.addEventListener("open", () => {
      openedThisAttempt = true;
      everOpened = true;
      failedAttemptsSinceOpen = 0;
      setStatus("Connected");
      // Replay every active subscription that hasn't been rejected this
      // session. Server state was wiped on disconnect.
      const toResubscribe: string[] = [];
      handlers.forEach((_, channel) => {
        if (!rejected.has(channel)) {
          toResubscribe.push(channel);
        }
      });
      sendSubscribe(toResubscribe);
    });

    socket.addEventListener("message", (event) => {
      const data = typeof event.data === "string" ? event.data : String(event.data);
      handleServerFrame(data);
    });

    socket.addEventListener("close", () => {
      socket = null;
      accepted.clear();
      if (!openedThisAttempt) {
        failedAttemptsSinceOpen += 1;
      }
      if (!everOpened && failedAttemptsSinceOpen >= MAX_FAILED_CONNECT_ATTEMPTS) {
        setStatus("Disconnected");
        return;
      }
      setStatus("Reconnecting...");
      reconnectTimer = window.setTimeout(connect, RECONNECT_DELAY_MS);
    });

    socket.addEventListener("error", () => {
      // The close handler is the source of truth for retry decisions; the
      // error event is informational.
    });
  };

  // Lazy connect: only fire up the websocket when the first subscriber
  // appears. Avoids opening a socket for unauthenticated landing pages.
  const ensureConnected = () => {
    if (socket || reconnectTimer !== null) return;
    if (status === "Disconnected" && failedAttemptsSinceOpen >= MAX_FAILED_CONNECT_ATTEMPTS) {
      // We've already given up this session; subscribers won't get live data
      // but their handler is still registered for when grants change after
      // a refresh.
      return;
    }
    connect();
  };

  const subscribe: SubscriptionClient["subscribe"] = (channels, handler) => {
    const newChannels: string[] = [];
    for (const channel of channels) {
      let set = handlers.get(channel);
      if (!set) {
        set = new Set();
        handlers.set(channel, set);
        newChannels.push(channel);
      }
      set.add(handler);
    }

    ensureConnected();
    // Send subscribe only for channels we haven't already asked the server
    // about. Rejected channels stay in `rejected` and won't get a duplicate
    // subscribe — but their handler is still wired so a future `invalidate`
    // or reconnect-after-permission-grant can wake them up.
    const toSend = newChannels.filter((c) => !accepted.has(c) && !rejected.has(c));
    sendSubscribe(toSend);

    return () => {
      const toUnsubscribe: string[] = [];
      for (const channel of channels) {
        const set = handlers.get(channel);
        if (!set) continue;
        set.delete(handler);
        if (set.size === 0) {
          handlers.delete(channel);
          if (accepted.has(channel)) {
            accepted.delete(channel);
            toUnsubscribe.push(channel);
          } else {
            rejected.delete(channel);
          }
        }
      }
      sendUnsubscribe(toUnsubscribe);
    };
  };

  return {
    subscribe,
    getStatus: () => status,
    onStatusChange: (listener) => {
      statusListeners.add(listener);
      return () => statusListeners.delete(listener);
    },
  };
}

// Test seam: drops the singleton so unit tests can construct a fresh client.
export function __resetSubscriptionClientForTests(): void {
  singleton = null;
}
