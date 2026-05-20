import { useEffect, useRef, useState } from "react";
import { NoteDto, updateNote as patchNote } from "@/api/content";
import { useUpdateNote } from "@/hooks/useContent";
import { useYjsDocument, type YjsDocumentHandle } from "@/lib/yjs/useYjsDocument";
import { useYjsDrawio } from "@/lib/yjs/useYjsDrawio";
import { ConnectionStatusPill } from "@/lib/yjs/ConnectionStatusPill";
import { EditableNoteTitle } from "./EditableNoteTitle";
import { notesTheme } from "./notesTheme";

// Debounce window for the SVG snapshot. Matches NapkinEditor's window so
// the page-embed renderer sees previews land at the same rough cadence
// across kinds.
const SVG_SNAPSHOT_DEBOUNCE_MS = 1500;

type Props = {
  note: NoteDto | null;
  noteName: string;
  // When set, the embed loads this revision's XML and runs in a view-only
  // flavor — autosave is disabled in the load action and any incoming
  // autosave/save events are ignored. versionNumber feeds the iframe key
  // so drawio remounts cleanly between revisions. Revisions bypass Yjs.
  revisionOverride?: {
    versionNumber: number;
    title: string | null;
    contentJsonb: string;
  } | null;
};

// drawio is vendored under public/drawio/ (fetched via `npm run fetch:drawio`)
// and served by Vite from the SPA's own origin at /drawio/. The iframe is
// therefore same-origin, and we identify messages by checking `e.source`
// against the iframe's contentWindow rather than `e.origin` against a hard-
// coded URL — that way we don't have to know the dev / prod origin at
// compile time and we still ignore messages coming from anywhere else.
//
// Embed URL parameters — see https://www.drawio.com/doc/faq/embed-mode for
// the full list. NOTE: `autosave=1` here is informational only — the flag
// that actually enables autosave events is sent through the `load` action
// message below (`autosave: 1`). See viewer.min.js: `t = 1 == I.autosave`
// inside the "load"==I.action branch. Without that, drawio never attaches
// its CHANGE → postMessage("autosave") listener and edits silently vanish.
function buildEmbedUrl({ readonly }: { readonly: boolean }): string {
  const params: Record<string, string> = {
    embed: "1",
    ui: "atlas",
    proto: "json",
    libraries: "1",
    noSaveBtn: "1",
    saveAndExit: "0",
    spin: "1"
  };
  if (readonly) {
    // drawio's readonly mode hides editing tools and prevents most
    // graph mutations. Defense-in-depth alongside our server-side
    // Hocuspocus readOnly enforcement — even if the SPA is modified
    // the server rejects writes from viewer connections.
    params.readonly = "1";
  }
  return `/drawio/index.html?${new URLSearchParams(params).toString()}`;
}

// Built once at module load. The live editor iframe stays mounted across
// diagram-note swaps; we re-load XML through drawio's `load` action rather
// than remounting (which would force a full drawio re-parse / re-init on
// every diagram open).
const LIVE_EMBED_URL = buildEmbedUrl({ readonly: false });
const REVISION_EMBED_URL = buildEmbedUrl({ readonly: true });

// Diagram editor backed by the diagrams.net (draw.io) iframe embed. Phase 4
// routes the XML through a Y.Text container synced via Hocuspocus:
//   draw.io → parent  { event: "init" | "autosave" | "save" }
//   parent  → draw.io { action: "load", xml: "...", autosave: 1 }
// On `autosave` from drawio we push to the Y.Text; on remote Y.Text changes
// we send a fresh `load` so the iframe re-renders. Known UX wart: a remote
// edit resets the local user's zoom / pan / selection, because drawio has
// no incremental "patch the scene" protocol.
export function DiagramEditor({ note, noteName, revisionOverride }: Props) {
  const viewingRevision = revisionOverride != null;
  const effectiveTitle = revisionOverride?.title ?? note?.title ?? noteName;
  const updateNote = useUpdateNote(note?.pageId ?? null);

  if (viewingRevision && revisionOverride) {
    return (
      <DiagramShell
        title={effectiveTitle}
        readOnlyTitle
        onTitleSave={() => {}}
        rightSlot={null}
      >
        <RevisionDrawioIframe
          // Re-mount on version swap so drawio re-sends `init` and we
          // push the new revision's XML on the load action.
          key={revisionOverride.versionNumber}
          xml={parseXml(revisionOverride.contentJsonb) ?? ""}
        />
      </DiagramShell>
    );
  }

  if (!note) {
    return (
      <DiagramShell
        title={noteName}
        readOnlyTitle
        onTitleSave={() => {}}
        rightSlot={null}
      >
        <div style={{ color: notesTheme.muted, fontSize: 13, padding: 24 }}>
          Select a note to start diagramming.
        </div>
      </DiagramShell>
    );
  }

  // No `key={note.id}` here on purpose — remounting the iframe per note
  // swap was the source of the multi-second cold start every time the
  // user clicked into a diagram. The persistent iframe inside LiveDiagram
  // swaps XML via postMessage instead.
  return (
    <LiveDiagram
      note={note}
      title={effectiveTitle}
      onTitleSave={(next) => updateNote.mutate({ id: note.id, body: { title: next } })}
    />
  );
}

function LiveDiagram({
  note,
  title,
  onTitleSave
}: {
  note: NoteDto;
  title: string;
  onTitleSave: (next: string) => void;
}) {
  const { handle, status, role } = useYjsDocument(`diagram:${note.id}`);
  const viewer = role === "viewer";

  // Viewer-role + we already have a snapshot → render the SVG inline and
  // skip the drawio iframe boot entirely. The snapshot is generated by
  // editor-role autosaves (see DrawioBody export path), so it's what a
  // read-only collaborator would see anyway.
  if (viewer && note.previewSvg) {
    return (
      <DiagramShell
        title={title}
        readOnlyTitle={false}
        onTitleSave={onTitleSave}
        rightSlot={<ConnectionStatusPill status={status} role={role} />}
      >
        <ViewerSvgPane svg={note.previewSvg} title={title} />
      </DiagramShell>
    );
  }

  return (
    <DiagramShell
      title={title}
      readOnlyTitle={false}
      onTitleSave={onTitleSave}
      rightSlot={<ConnectionStatusPill status={status} role={role} />}
    >
      <PersistentDrawioIframe note={note} handle={handle} viewer={viewer} />
    </DiagramShell>
  );
}

// Persistent drawio iframe — mounted once and kept alive as long as the
// user stays on a diagram tab inside /notes. On note swap, the inner
// NoteBinding (keyed by note.id) re-runs its `load` effect and pushes
// fresh XML to the already-running iframe instead of reparsing drawio's
// ~3 MB of JS from scratch.
function PersistentDrawioIframe({
  note,
  handle,
  viewer
}: {
  note: NoteDto;
  handle: YjsDocumentHandle | null;
  viewer: boolean;
}) {
  const iframeRef = useRef<HTMLIFrameElement | null>(null);
  const [iframeReady, setIframeReady] = useState(false);

  // Bindings written by NoteBinding on each note swap and read by the
  // single message-listener below. Refs (rather than re-registering a
  // listener per note) keep the listener lifetime tied to the iframe.
  const onLocalXmlRef = useRef<((xml: string) => void) | null>(null);
  const noteIdRef = useRef<string | null>(null);
  const snapshotTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lastSentSvgRef = useRef<string | null>(null);

  useEffect(() => {
    const onMessage = (e: MessageEvent) => {
      const target = iframeRef.current?.contentWindow;
      // Only accept messages from our own iframe — same-origin, but other
      // iframes (chatbot, etc.) could be present on the page.
      if (!target || e.source !== target) return;
      let data: Record<string, unknown>;
      try {
        data = typeof e.data === "string" ? JSON.parse(e.data) : (e.data as Record<string, unknown>);
      } catch {
        return;
      }
      if (!data || typeof data.event !== "string") return;

      switch (data.event) {
        case "init":
        case "configure": {
          // First-time boot — flip ready so the active NoteBinding's load
          // effect runs and pushes its note's XML. Subsequent note swaps
          // do NOT trigger another init because the iframe stays mounted.
          setIframeReady(true);
          // drawio's internal beforeunload guard ("All changes will be
          // lost") is redundant now that Yjs+IndexedDB owns durability —
          // disable it. Same-origin iframe, so direct property assignment
          // works.
          try {
            target.onbeforeunload = null;
          } catch {
            // cross-origin guard — shouldn't happen for /drawio/
          }
          break;
        }
        case "autosave":
        case "save": {
          const cb = onLocalXmlRef.current;
          // Viewer (no cb) or transient between-notes window: drop the event.
          if (!cb) return;
          try {
            target.onbeforeunload = null;
          } catch {
            // ignore
          }
          const xml = typeof data.xml === "string" ? data.xml : "";
          if (xml) cb(xml);
          // Schedule a debounced SVG export keyed to the note id that
          // owned the autosave at the moment it landed. If the user
          // swaps notes mid-debounce, NoteBinding clears this timer.
          const nid = noteIdRef.current;
          if (nid) {
            if (snapshotTimerRef.current) clearTimeout(snapshotTimerRef.current);
            snapshotTimerRef.current = setTimeout(() => {
              snapshotTimerRef.current = null;
              const w = iframeRef.current?.contentWindow;
              if (!w) return;
              w.postMessage(
                JSON.stringify({ action: "export", format: "xmlsvg" }),
                "*"
              );
            }, SVG_SNAPSHOT_DEBOUNCE_MS);
          }
          break;
        }
        case "export": {
          // Reply to our `action: "export"` request above. drawio's
          // xmlsvg export returns a data: URL in `data` — strip the
          // prefix and decode to a plain SVG string before PATCHing.
          //
          // Fire-and-forget on purpose: invalidating the notes query
          // here would churn the live Yjs/drawio editor mid-edit (see
          // the same rationale in NapkinEditor's snapshot scheduler).
          const nid = noteIdRef.current;
          if (!nid) return;
          const raw = typeof data.data === "string" ? data.data : "";
          const svg = decodeDrawioExportPayload(raw);
          if (!svg || svg === lastSentSvgRef.current) return;
          lastSentSvgRef.current = svg;
          void patchNote(nid, { previewSvg: svg }).catch((err) => {
            console.warn("[DiagramEditor] previewSvg PATCH failed:", err);
          });
          break;
        }
        default:
          // "exit", "exportComplete", etc. — ignored.
          break;
      }
    };
    window.addEventListener("message", onMessage);
    return () => window.removeEventListener("message", onMessage);
  }, []);

  // Final-unmount cleanup — drop any pending snapshot timer when the
  // user leaves /notes entirely (DiagramEditor itself unmounts).
  useEffect(() => {
    return () => {
      if (snapshotTimerRef.current) clearTimeout(snapshotTimerRef.current);
    };
  }, []);

  return (
    <div style={{ flex: 1, minHeight: 0, position: "relative" }}>
      <iframe
        ref={iframeRef}
        src={LIVE_EMBED_URL}
        title="Draw.io diagram editor"
        style={{ width: "100%", height: "100%", border: "none", display: "block" }}
      />
      {handle ? (
        <NoteBinding
          // Keyed on noteId so a note swap remounts the binding (cheap —
          // it renders nothing) while the iframe sibling stays alive.
          key={note.id}
          note={note}
          handle={handle}
          viewer={viewer}
          iframeRef={iframeRef}
          iframeReady={iframeReady}
          onLocalXmlRef={onLocalXmlRef}
          noteIdRef={noteIdRef}
          snapshotTimerRef={snapshotTimerRef}
          lastSentSvgRef={lastSentSvgRef}
        />
      ) : null}
    </div>
  );
}

// Per-note glue. Wires the active note's Yjs handle into the parent's
// persistent iframe via refs, and pushes a `load` action so drawio renders
// this note's XML. Renders nothing — pure side-effect.
function NoteBinding({
  note,
  handle,
  viewer,
  iframeRef,
  iframeReady,
  onLocalXmlRef,
  noteIdRef,
  snapshotTimerRef,
  lastSentSvgRef
}: {
  note: NoteDto;
  handle: YjsDocumentHandle;
  viewer: boolean;
  iframeRef: React.MutableRefObject<HTMLIFrameElement | null>;
  iframeReady: boolean;
  onLocalXmlRef: React.MutableRefObject<((xml: string) => void) | null>;
  noteIdRef: React.MutableRefObject<string | null>;
  snapshotTimerRef: React.MutableRefObject<ReturnType<typeof setTimeout> | null>;
  lastSentSvgRef: React.MutableRefObject<string | null>;
}) {
  const { getCurrentXml, onRemoteXml, pushLocalXml } = useYjsDrawio({
    doc: handle.doc,
    provider: handle.provider
  });

  // Wire per-note callbacks into the parent's message listener and reset
  // dedupe state so the first save of this note isn't compared against
  // the previous note's SVG. Cancel any pending snapshot timer left over
  // from the previous note — its target id would be stale.
  useEffect(() => {
    noteIdRef.current = viewer ? null : note.id;
    onLocalXmlRef.current = viewer ? null : pushLocalXml;
    lastSentSvgRef.current = null;
    if (snapshotTimerRef.current) {
      clearTimeout(snapshotTimerRef.current);
      snapshotTimerRef.current = null;
    }
    return () => {
      // On unmount (note swap), null the bindings so any straggler
      // messages from the iframe don't get dispatched to this note.
      noteIdRef.current = null;
      onLocalXmlRef.current = null;
    };
  }, [note.id, viewer, pushLocalXml, noteIdRef, onLocalXmlRef, snapshotTimerRef, lastSentSvgRef]);

  // Push the load action whenever the iframe is ready and this note's
  // handle is wired up. This is what makes diagram-note swaps fast: the
  // iframe never reloads — drawio just re-renders with the new XML.
  useEffect(() => {
    if (!iframeReady) return;
    const target = iframeRef.current?.contentWindow;
    if (!target) return;
    target.postMessage(
      JSON.stringify({
        action: "load",
        xml: getCurrentXml(),
        autosave: viewer ? 0 : 1
      }),
      "*"
    );
    try {
      target.onbeforeunload = null;
    } catch {
      // ignore
    }
  }, [iframeReady, note.id, viewer, getCurrentXml, iframeRef]);

  // Subscribe to remote Y.Text changes. When another collaborator's XML
  // arrives, push a fresh `load` action — drawio has no per-shape diff
  // protocol so it re-renders the entire scene. Local-origin transactions
  // are filtered inside useYjsDrawio so our own autosave doesn't cause a
  // self-reload.
  useEffect(() => {
    const unsubscribe = onRemoteXml((xml) => {
      const target = iframeRef.current?.contentWindow;
      if (!target) return;
      target.postMessage(
        JSON.stringify({ action: "load", xml, autosave: viewer ? 0 : 1 }),
        "*"
      );
    });
    return unsubscribe;
  }, [onRemoteXml, viewer, iframeRef]);

  return null;
}

// Single-shot revision viewer. Kept separate from PersistentDrawioIframe
// because revisions are load-once-and-display: there's no Yjs binding,
// no autosave, no note-swap state to track. The parent keys this on
// versionNumber so picking a different revision triggers a fresh iframe.
function RevisionDrawioIframe({ xml }: { xml: string }) {
  const iframeRef = useRef<HTMLIFrameElement | null>(null);

  useEffect(() => {
    const onMessage = (e: MessageEvent) => {
      const target = iframeRef.current?.contentWindow;
      if (!target || e.source !== target) return;
      let data: Record<string, unknown>;
      try {
        data = typeof e.data === "string" ? JSON.parse(e.data) : (e.data as Record<string, unknown>);
      } catch {
        return;
      }
      if (!data || typeof data.event !== "string") return;
      if (data.event === "init" || data.event === "configure") {
        target.postMessage(
          JSON.stringify({ action: "load", xml, autosave: 0 }),
          "*"
        );
        try {
          target.onbeforeunload = null;
        } catch {
          // ignore
        }
      }
    };
    window.addEventListener("message", onMessage);
    return () => window.removeEventListener("message", onMessage);
  }, [xml]);

  return (
    <div style={{ flex: 1, minHeight: 0, position: "relative" }}>
      <iframe
        ref={iframeRef}
        src={REVISION_EMBED_URL}
        title="Draw.io diagram (revision)"
        style={{ width: "100%", height: "100%", border: "none", display: "block" }}
      />
    </div>
  );
}

// Viewer-role pane: render the latest editor-generated SVG snapshot
// directly. data: URI form — img-mode SVG cannot execute scripts, which
// removes the foreignObject/inline-script XSS surface that drawio exports
// sometimes carry. Mirrors src/lib/blocknote/noteEmbedBlock.tsx.
function ViewerSvgPane({ svg, title }: { svg: string; title: string }) {
  const src = `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
  return (
    <div
      style={{
        flex: 1,
        minHeight: 0,
        background: "#fff",
        overflow: "auto",
        display: "flex",
        alignItems: "flex-start",
        justifyContent: "center",
        padding: 16
      }}
    >
      <img
        src={src}
        alt={title ? `${title} (read-only snapshot)` : "Diagram (read-only snapshot)"}
        style={{
          maxWidth: "100%",
          height: "auto",
          display: "block"
        }}
      />
    </div>
  );
}

function DiagramShell({
  title,
  readOnlyTitle,
  onTitleSave,
  rightSlot,
  children
}: {
  title: string;
  readOnlyTitle: boolean;
  onTitleSave: (next: string) => void;
  rightSlot: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <div
      style={{
        flex: 1,
        display: "flex",
        flexDirection: "column",
        minHeight: 0,
        background: "#fff"
      }}
    >
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 12,
          padding: "8px 40px",
          borderBottom: `1px solid ${notesTheme.border}`,
          background: "#fff",
          flexShrink: 0
        }}
      >
        <div style={{ flex: 1, minWidth: 0 }}>
          <EditableNoteTitle
            value={title}
            readOnly={readOnlyTitle}
            onSave={onTitleSave}
            style={{ fontSize: 18, fontWeight: 700, color: notesTheme.dark }}
          />
        </div>
        {rightSlot}
      </div>
      {children}
    </div>
  );
}

// drawio's `action: "export", format: "xmlsvg"` reply puts the SVG in
// `data.data` as a data URL (`data:image/svg+xml;base64,...`). Strip the
// prefix and decode so we can ship plain SVG markup to the backend (the
// page-embed renderer wraps it in its own data URL again via encodeURIComponent).
function decodeDrawioExportPayload(raw: string): string | null {
  if (!raw) return null;
  // Sometimes drawio returns raw SVG (no data: prefix) for newer builds;
  // accept both for resilience.
  if (raw.trimStart().startsWith("<svg")) return raw;
  const match = /^data:image\/svg\+xml(?:;[^,]*)?,(.*)$/i.exec(raw);
  if (!match) return null;
  const payload = match[1];
  try {
    // Heuristic: the comma-delimited payload is base64 unless it starts
    // with a `%` (URL-encoded). drawio's exporter uses base64 by default.
    if (payload.startsWith("%")) return decodeURIComponent(payload);
    return atob(payload);
  } catch {
    return null;
  }
}

// Stored value shape: `{ "type": "drawio", "version": 1, "xml": "..." }`
// wrapped in JSONB. parseXml extracts the XML string. Used only by the
// revision-view branch — live edits' initialXml comes from the Y.Text via
// useYjsDrawio.
function parseXml(raw: string | null | undefined): string | null {
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw);
    if (parsed && typeof parsed === "object" && typeof parsed.xml === "string") {
      return parsed.xml;
    }
  } catch {
    if (raw.trimStart().startsWith("<")) return raw;
  }
  return null;
}
