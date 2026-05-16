import { useEffect, useMemo, useRef } from "react";
import * as Y from "yjs";
import type { HocuspocusProvider } from "@hocuspocus/provider";

// Tag every local-originated transaction with this symbol so our remote-
// change observer doesn't replay the user's own writes back into the
// drawio iframe (which would force a scene re-init on every keystroke).
const LOCAL_ORIGIN = Symbol("yjs-drawio-local");

export interface UseYjsDrawioResult {
  // XML to feed the iframe's initial `load` postMessage. Reflects the
  // Y.Text state captured at hook-mount time.
  initialXml: string;
  // Subscribe to remote XML updates. Returns the unsubscribe function.
  // The caller is expected to postMessage a fresh `load` action to the
  // drawio iframe — drawio has no per-shape diff protocol, so any remote
  // edit re-loads the entire scene. (Known UX limitation: re-load resets
  // the local user's zoom / pan / selection.)
  onRemoteXml: (cb: (xml: string) => void) => () => void;
  // Replace the doc's XML with the user's latest autosave payload.
  // Tagged LOCAL_ORIGIN so onRemoteXml subscribers don't fire for our
  // own writes.
  pushLocalXml: (xml: string) => void;
}

// Bidirectional sync of a drawio mxfile XML string through a Yjs Y.Text.
//
// Y.Text is overkill for drawio's whole-XML autosave events (we replace
// the whole text every save, never doing fine-grained character ops),
// but it gives us the Yjs CRDT merge semantics for free and leaves a
// path open to true op-level collab if/when drawio grows that protocol.
//
// Y.Doc layout:
//   doc.getText("xml") — the full <mxfile>...</mxfile> string
export function useYjsDrawio(args: {
  doc: Y.Doc;
  provider: HocuspocusProvider;
}): UseYjsDrawioResult {
  const { doc } = args;

  const ytext = useMemo(() => doc.getText("xml"), [doc]);

  // Captured once at hook-mount. Subsequent changes go through the
  // observer / postMessage path so we never re-mount the iframe purely
  // because the React tree re-rendered.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const initialXml = useMemo<string>(() => ytext.toString(), []);

  // Subscriber registry. We expose a thin subscribe API rather than
  // a useEffect-from-the-consumer pattern so DiagramEditor can wire the
  // callback to whichever postMessage target is current at the time
  // (the iframeRef.current.contentWindow).
  const subscribersRef = useRef<Set<(xml: string) => void>>(new Set());

  useEffect(() => {
    const onYTextChange = (
      _event: Y.YTextEvent,
      transaction: Y.Transaction
    ) => {
      if (transaction.origin === LOCAL_ORIGIN) return;
      const xml = ytext.toString();
      for (const cb of subscribersRef.current) cb(xml);
    };
    ytext.observe(onYTextChange);
    return () => {
      ytext.unobserve(onYTextChange);
    };
  }, [ytext]);

  const onRemoteXml = (cb: (xml: string) => void) => {
    subscribersRef.current.add(cb);
    return () => {
      subscribersRef.current.delete(cb);
    };
  };

  const pushLocalXml = (xml: string) => {
    if (ytext.toString() === xml) return;
    doc.transact(() => {
      ytext.delete(0, ytext.length);
      ytext.insert(0, xml);
    }, LOCAL_ORIGIN);
  };

  return { initialXml, onRemoteXml, pushLocalXml };
}
