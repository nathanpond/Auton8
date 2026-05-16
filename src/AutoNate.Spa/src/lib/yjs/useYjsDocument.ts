import { useEffect, useState } from "react";
import * as Y from "yjs";
import { HocuspocusProvider } from "@hocuspocus/provider";
import { IndexeddbPersistence } from "y-indexeddb";
import { fetchTicket, type YjsRole } from "./ticket";

export type YjsConnectionStatus = "connecting" | "connected" | "reconnecting" | "offline";

export interface YjsDocumentHandle {
  doc: Y.Doc;
  provider: HocuspocusProvider;
}

export interface UseYjsDocumentResult {
  handle: YjsDocumentHandle | null;
  status: YjsConnectionStatus;
  // Server-decided role for THIS connection. Starts as "viewer" so the
  // editor renders read-only until the first ticket fetch returns —
  // safer than the inverse (editor flashing edit-capable, user starts
  // typing, role downgrades, characters lost). Updates from every ticket
  // fetch; covers permission changes between sessions.
  role: YjsRole;
}

// Hocuspocus WebSocket URL. In dev this is the docker-exposed port on
// localhost; in prod it's set by the deploy. The vite env var is set at
// build time; the fallback matches the YjsServerOptions default and the
// docker-compose dev port.
const HOCUSPOCUS_WS_URL: string =
  (import.meta.env.VITE_HOCUSPOCUS_WS_URL as string | undefined) ?? "ws://localhost:1234";

// Opens a Y.Doc plus a HocuspocusProvider keyed on a document name like
// "page:<guid>" or "note:<guid>". The provider authenticates via a
// short-lived ticket minted by .NET — the `token` callback re-fetches a
// fresh ticket on each connection attempt (tickets are single-use, so
// reconnects need a new one).
//
// Also attaches an IndexedDB persistence layer (`y-indexeddb`) so:
//   - documents load instantly on subsequent visits from local cache
//   - edits made while offline accumulate locally and merge into
//     Hocuspocus's state on reconnect via Yjs's CRDT semantics
// The two providers share the same Y.Doc; Yjs handles concurrent writes
// without conflict.
//
// Returns `handle: null` while the resources are being constructed during
// the first render (one-tick delay because we initialize inside useEffect
// to scope the cleanup). Callers should render a "connecting" placeholder
// in that window.
export function useYjsDocument(documentName: string | null): UseYjsDocumentResult {
  const [handle, setHandle] = useState<YjsDocumentHandle | null>(null);
  // Lazy-initialize from navigator.onLine so a hard reload while offline
  // shows the "Offline" state immediately rather than flashing
  // "Connecting…" first.
  const [hocuspocusStatus, setHocuspocusStatus] =
    useState<YjsConnectionStatus>("connecting");
  const [browserOnline, setBrowserOnline] = useState<boolean>(() =>
    typeof navigator === "undefined" ? true : navigator.onLine
  );
  // Default to "viewer" so consumers render read-only chrome until the
  // first ticket fetch lands. Editors briefly see "View only" before it
  // upgrades to "Live" — a tolerable flash vs. the alternative of a
  // viewer typing into a soon-to-be-rejected session.
  const [role, setRole] = useState<YjsRole>("viewer");

  // Browser online/offline events. Hocuspocus's status alone isn't enough
  // to distinguish "server is down but I have a network" from "I have no
  // network at all" — both look like "reconnecting" to the provider.
  useEffect(() => {
    const onOnline = () => setBrowserOnline(true);
    const onOffline = () => setBrowserOnline(false);
    window.addEventListener("online", onOnline);
    window.addEventListener("offline", onOffline);
    return () => {
      window.removeEventListener("online", onOnline);
      window.removeEventListener("offline", onOffline);
    };
  }, []);

  useEffect(() => {
    if (!documentName) {
      setHandle(null);
      setHocuspocusStatus("offline");
      return;
    }

    const doc = new Y.Doc();

    // IndexedDB cache. Keyed on the documentName so each page/note has
    // its own entry. The persistence object syncs the doc both ways:
    // loads cached state on mount, writes incoming updates back to the
    // store.
    const indexeddb = new IndexeddbPersistence(documentName, doc);

    const provider = new HocuspocusProvider({
      url: HOCUSPOCUS_WS_URL,
      name: documentName,
      document: doc,
      token: async () => {
        const t = await fetchTicket(documentName);
        // Capture the role each time we fetch a ticket. On reconnect
        // .NET recomputes against the live grant — covers permission
        // changes between sessions.
        setRole(t.role);
        return t.ticket;
      }
    });

    const onStatus = (event: { status: string }) => {
      if (event.status === "connected") setHocuspocusStatus("connected");
      else if (event.status === "connecting") setHocuspocusStatus("connecting");
      else setHocuspocusStatus("reconnecting");
    };
    const onDisconnect = () => setHocuspocusStatus("reconnecting");
    const onAuthFailed = (data: unknown) => {
      console.error(`[yjs] ${documentName} authentication-failed:`, data);
    };
    provider.on("status", onStatus);
    provider.on("disconnect", onDisconnect);
    provider.on("authenticationFailed", onAuthFailed);

    setHandle({ doc, provider });

    // Force-reconnect on tab focus after a long idle. Reasons:
    //   - The Yjs ticket is HMAC-signed against the user's permissions at
    //     mint time, and Hocuspocus's `connection.readOnly` is decided at
    //     auth time. If an admin demotes / promotes the user while the
    //     tab was backgrounded, the in-flight session keeps the stale
    //     role until next reconnect. Forcing reconnect after a long idle
    //     trades a brief sync for fresh authorization.
    //   - The 5-minute threshold avoids reconnect storms when users
    //     quickly tab-switch.
    const IDLE_RECONNECT_THRESHOLD_MS = 5 * 60_000;
    let hiddenAt: number | null = null;
    const onVisibility = () => {
      if (document.visibilityState === "hidden") {
        hiddenAt = Date.now();
        return;
      }
      const idleMs = hiddenAt ? Date.now() - hiddenAt : 0;
      hiddenAt = null;
      if (idleMs >= IDLE_RECONNECT_THRESHOLD_MS) {
        provider.disconnect();
        provider.connect();
      }
    };
    document.addEventListener("visibilitychange", onVisibility);

    return () => {
      document.removeEventListener("visibilitychange", onVisibility);
      provider.off("status", onStatus);
      provider.off("disconnect", onDisconnect);
      provider.off("authenticationFailed", onAuthFailed);
      provider.destroy();
      void indexeddb.destroy();
      doc.destroy();
      setHandle(null);
    };
  }, [documentName]);

  // Compose the user-visible status. The browser-offline signal wins
  // because it's the unambiguous "you can keep editing, we just can't
  // sync right now" state. Otherwise we surface whatever Hocuspocus
  // reports.
  const status: YjsConnectionStatus =
    !browserOnline && handle ? "offline" : hocuspocusStatus;

  return { handle, status, role };
}
