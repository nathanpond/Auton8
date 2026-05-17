import { useCallback, useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Alert, Button } from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import { useBusSubscription } from "@/hooks/useBusSubscription";
import { useSubscriptionStatus } from "@/hooks/useSubscriptionStatus";
import { useMe } from "@/hooks/useMe";
import { ChannelEvent } from "@/lib/ws/subscription";
import { getDaprSidecarStatus } from "@/api/health";
import "./BusWatcher.css";

type LoggedEvent = {
  receivedAtUtc: string;
  topic: string;
  contentType?: string | null;
  headers: Record<string, string>;
  payload: string;
};

const MAX_ENTRIES = 250;

export default function BusWatcher() {
  const { data: me } = useMe();
  const isSuperAdmin = me?.authenticated ? me.isSuperAdmin : false;
  const [entries, setEntries] = useState<LoggedEvent[]>([]);
  const entriesRef = useRef(entries);
  entriesRef.current = entries;

  // firehose:all delivers every bus message the server sees; subscribe gate
  // requires SuperAdmin so non-admins are server-rejected (the nav entry
  // also hides the page link below).
  useBusSubscription(
    isSuperAdmin ? ["firehose:all"] : [],
    useCallback((event) => {
      if (event.type !== "event") return;
      const channelEvent = event as ChannelEvent;
      setEntries((prev) => {
        const next = [...prev, {
          receivedAtUtc: channelEvent.receivedAtUtc,
          topic: channelEvent.topic,
          contentType: channelEvent.contentType ?? null,
          headers: channelEvent.headers,
          payload: channelEvent.payload,
        }];
        if (next.length > MAX_ENTRIES) {
          next.splice(0, next.length - MAX_ENTRIES);
        }
        return next;
      });
    }, []),
  );

  const status = useSubscriptionStatus();

  const { data: daprStatus } = useQuery({
    queryKey: ["health", "dapr"],
    queryFn: ({ signal }) => getDaprSidecarStatus(signal),
    staleTime: 30_000
  });

  const startupWarning =
    daprStatus && !daprStatus.available
      ? "The web app is running without a reachable Dapr sidecar, so workflow pub/sub events will not arrive here. Start AutoNate with `make app-dapr` or a Rider run configuration that launches the app through Dapr."
      : null;

  const clearLog = () => setEntries([]);

  if (me?.authenticated && !isSuperAdmin) {
    return (
      <>
        <PageHeader
          title="Bus Watcher"
          description="Watch every workflow bus event the app consumes and stream it into a live log window."
        />
        <Alert color="red" variant="light" role="alert">
          The Bus Watcher live stream is restricted to SuperAdmins.
        </Alert>
      </>
    );
  }

  return (
    <>
      <PageHeader
        title="Bus Watcher"
        description="Watch every workflow bus event the app consumes and stream it into a live log window."
      />

      <div className="bus-watcher-toolbar">
        <span className={`bus-watcher-status ${statusClass(status)}`}>{status}</span>
        <Button variant="default" onClick={clearLog} title="Clear log">
          Clear Log
        </Button>
      </div>

      {startupWarning && (
        <div className="bus-watcher-warning" role="alert">
          {startupWarning}
        </div>
      )}

      <div className="bus-watcher-shell">
        {entries.length === 0 ? (
          <p className="bus-watcher-empty">Waiting for bus traffic...</p>
        ) : (
          <div
            className="bus-watcher-log"
            role="log"
            aria-live="polite"
            aria-label="Bus event log"
          >
            {entries.map((entry, idx) => (
              <article key={`${entry.receivedAtUtc}-${idx}`} className="bus-watcher-entry">
                <div className="bus-watcher-entry-meta">
                  <span className="bus-watcher-entry-time">
                    {formatTimestamp(entry.receivedAtUtc)}
                  </span>
                  <span className="bus-watcher-entry-topic">{entry.topic}</span>
                </div>

                {(entry.contentType || Object.keys(entry.headers ?? {}).length > 0) && (
                  <pre className="bus-watcher-entry-headers">{formatHeaders(entry)}</pre>
                )}

                <pre className="bus-watcher-entry-payload">{entry.payload}</pre>
              </article>
            ))}
          </div>
        )}
      </div>
    </>
  );
}

function statusClass(status: string): string {
  if (status.startsWith("Connected")) {
    return "bus-watcher-status-connected";
  }
  return "bus-watcher-status-connecting";
}

function formatTimestamp(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }

  const pad = (n: number) => String(n).padStart(2, "0");
  const y = date.getFullYear();
  const m = pad(date.getMonth() + 1);
  const d = pad(date.getDate());
  const hh = pad(date.getHours());
  const mm = pad(date.getMinutes());
  const ss = pad(date.getSeconds());
  const offsetMinutes = -date.getTimezoneOffset();
  const sign = offsetMinutes >= 0 ? "+" : "-";
  const absOffset = Math.abs(offsetMinutes);
  const offH = pad(Math.floor(absOffset / 60));
  const offM = pad(absOffset % 60);
  return `${y}-${m}-${d} ${hh}:${mm}:${ss} ${sign}${offH}:${offM}`;
}

function formatHeaders(entry: LoggedEvent): string {
  const lines: string[] = [];
  if (entry.contentType) {
    lines.push(`content-type: ${entry.contentType}`);
  }
  for (const [key, value] of Object.entries(entry.headers ?? {})) {
    if (key.toLowerCase() === "content-type") {
      continue;
    }
    lines.push(`${key}: ${value}`);
  }
  return lines.join("\n");
}
