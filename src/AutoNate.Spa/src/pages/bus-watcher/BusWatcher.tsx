import { useCallback, useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useBusConnection, BusMessageEnvelope } from "@/hooks/useBusConnection";
import { getDaprSidecarStatus } from "@/api/health";
import "./BusWatcher.css";

const MAX_ENTRIES = 250;

export default function BusWatcher() {
  const [entries, setEntries] = useState<BusMessageEnvelope[]>([]);
  const entriesRef = useRef(entries);
  entriesRef.current = entries;

  const onMessage = useCallback((entry: BusMessageEnvelope) => {
    setEntries((prev) => {
      const next = [...prev, entry];
      if (next.length > MAX_ENTRIES) {
        next.splice(0, next.length - MAX_ENTRIES);
      }
      return next;
    });
  }, []);

  const { status } = useBusConnection({ onMessage });

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

  return (
    <>
      <div className="page-head">
        <div>
          <h1 className="page-header mb-1">Bus Watcher</h1>
          <p className="page-head-copy bus-watcher-copy">
            Watch every workflow bus event the app consumes and stream it into a live log window.
          </p>
        </div>
      </div>

      <div className="bus-watcher-toolbar">
        <span className={`bus-watcher-status ${statusClass(status)}`}>{status}</span>
        <button
          type="button"
          className="btn btn-outline-secondary"
          onClick={clearLog}
          title="Clear log"
        >
          Clear Log
        </button>
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

function formatHeaders(entry: BusMessageEnvelope): string {
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
