import { useEffect, useRef, useState } from "react";
import {
  BusConnectionStatus,
  WorkflowEventBusMessage,
  createBusConnection
} from "@/lib/ws/busWatcher";

export type BusMessageEnvelope = WorkflowEventBusMessage & { raw: string };

export type UseBusConnectionOptions = {
  enabled?: boolean;
  topicPrefix?: string;
  onMessage?: (message: BusMessageEnvelope) => void;
};

export function useBusConnection(options: UseBusConnectionOptions = {}) {
  const { enabled = true, topicPrefix, onMessage } = options;
  const [status, setStatus] = useState<BusConnectionStatus>("Connecting...");
  const [lastMessage, setLastMessage] = useState<BusMessageEnvelope | null>(null);
  const onMessageRef = useRef(onMessage);
  onMessageRef.current = onMessage;

  useEffect(() => {
    if (!enabled) {
      setStatus("Disconnected");
      return;
    }

    const connection = createBusConnection({
      onStatusChanged: (next) => setStatus(next),
      onMessage: (raw) => {
        try {
          const parsed = JSON.parse(raw) as WorkflowEventBusMessage;
          if (topicPrefix && !parsed.topic?.startsWith(topicPrefix)) {
            return;
          }

          const envelope: BusMessageEnvelope = { ...parsed, raw };
          setLastMessage(envelope);
          onMessageRef.current?.(envelope);
        } catch {
          // Swallow parse errors — a non-JSON frame is not useful to consumers.
        }
      }
    });

    return () => {
      connection.dispose();
    };
  }, [enabled, topicPrefix]);

  return { status, lastMessage };
}
