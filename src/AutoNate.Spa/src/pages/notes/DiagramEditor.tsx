import { useEffect, useRef } from "react";
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
function buildEmbedUrl(readonly: boolean): string {
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
        <DrawioIframe
          // Re-mount on version swap so drawio re-sends `init` and we
          // push the new revision's XML on the load action.
          key={revisionOverride.versionNumber}
          noteId={null}
          // Revision view: static snapshot, no Yjs binding. Wrap in a
          // function so DrawioIframe can use a single accessor shape
          // for both live and revision paths.
          getXml={() => parseXml(revisionOverride.contentJsonb) ?? ""}
          viewingRevision
          onLocalXml={null}
          subscribeRemoteXml={null}
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

  return (
    <LiveDiagram
      // Re-mount on note swap so the Yjs handle + iframe both refresh.
      key={note.id}
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

  return (
    <DiagramShell
      title={title}
      readOnlyTitle={false}
      onTitleSave={onTitleSave}
      rightSlot={<ConnectionStatusPill status={status} role={role} />}
    >
      {handle ? <DiagramBody note={note} handle={handle} viewer={role === "viewer"} /> : null}
    </DiagramShell>
  );
}

function DiagramBody({
  note,
  handle,
  viewer
}: {
  note: NoteDto;
  handle: YjsDocumentHandle;
  viewer: boolean;
}) {
  const { getCurrentXml, onRemoteXml, pushLocalXml } = useYjsDrawio({
    doc: handle.doc,
    provider: handle.provider
  });

  // Viewers reuse the revision-view code path: `readonly=1` in the embed
  // URL hides drawio's editing chrome and skips the pushLocalXml wiring,
  // so even a UI-modified viewer can't successfully autosave. The server
  // would reject any write anyway via Hocuspocus's connection.readOnly;
  // this is the UX layer.
  return (
    <DrawioIframe
      noteId={!viewer ? note.id : null}
      getXml={getCurrentXml}
      viewingRevision={viewer}
      onLocalXml={viewer ? null : pushLocalXml}
      subscribeRemoteXml={onRemoteXml}
    />
  );
}

function DrawioIframe({
  noteId,
  getXml,
  viewingRevision,
  onLocalXml,
  subscribeRemoteXml
}: {
  // Note id to PATCH the rendered SVG snapshot against. Null in viewer /
  // revision-view modes — skips the snapshot path entirely.
  noteId: string | null;
  // Just-in-time XML accessor. Called when drawio's iframe reports
  // `event: "init"` and is ready to accept a `load` action. Reading at
  // call time (rather than capturing at mount) avoids the race where
  // Hocuspocus's server-state sync hadn't completed at mount yet,
  // which made saved diagrams open blank.
  getXml: () => string;
  viewingRevision: boolean;
  // Called with each autosave XML from drawio. Null in revision-view mode
  // (drawio's autosave is disabled in the load action anyway, but the
  // null also short-circuits any straggler save events).
  onLocalXml: ((xml: string) => void) | null;
  // Subscribe to remote XML updates. Returns the unsubscribe function so
  // we can clean up on unmount. Null in revision-view mode.
  subscribeRemoteXml: ((cb: (xml: string) => void) => () => void) | null;
}) {
  const iframeRef = useRef<HTMLIFrameElement | null>(null);
  const snapshotTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lastSentSvgRef = useRef<string | null>(null);

  // Clear any pending snapshot timer on unmount so a tab swap can't fire a
  // snapshot against the previous iframe (whose contentWindow is gone).
  useEffect(() => {
    return () => {
      if (snapshotTimer.current) clearTimeout(snapshotTimer.current);
    };
  }, []);

  // postMessage listener for the iframe. Re-subscribed when the bindings
  // change (note swap re-keys the iframe and remounts this component
  // entirely, so this is mostly belt-and-braces).
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
          // Load the diagram. autosave is enabled only in the live editor;
          // for revision views we omit it so drawio doesn't fire change
          // events we'd have to suppress. Call getXml() at THIS moment
          // (not at mount) so Yjs sync has had time to land the saved
          // content into the Y.Text.
          target.postMessage(
            JSON.stringify({
              action: "load",
              xml: getXml(),
              autosave: viewingRevision ? 0 : 1
            }),
            "*"
          );
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
          if (viewingRevision || !onLocalXml) return;
          try {
            target.onbeforeunload = null;
          } catch {
            // ignore
          }
          const xml = typeof data.xml === "string" ? data.xml : "";
          if (xml) onLocalXml(xml);
          // Schedule a debounced SVG export so the page-embed renderer can
          // show this diagram inline. drawio's `export` action with
          // `format: "xmlsvg"` returns SVG (with embedded mxfile) on a
          // follow-up "export" message — handled below.
          if (noteId) {
            if (snapshotTimer.current) clearTimeout(snapshotTimer.current);
            snapshotTimer.current = setTimeout(() => {
              snapshotTimer.current = null;
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
          if (!noteId) return;
          const raw = typeof data.data === "string" ? data.data : "";
          const svg = decodeDrawioExportPayload(raw);
          if (!svg || svg === lastSentSvgRef.current) return;
          lastSentSvgRef.current = svg;
          void patchNote(noteId, { previewSvg: svg }).catch((err) => {
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
  }, [getXml, viewingRevision, onLocalXml, noteId]);

  // Subscribe to remote Y.Text changes. When another collaborator's XML
  // arrives, push a fresh `load` action — drawio has no per-shape diff
  // protocol so it re-renders the entire scene. Local-origin transactions
  // are filtered inside useYjsDrawio so our own autosave doesn't cause a
  // self-reload.
  useEffect(() => {
    if (!subscribeRemoteXml) return;
    const unsubscribe = subscribeRemoteXml((xml) => {
      const target = iframeRef.current?.contentWindow;
      if (!target) return;
      target.postMessage(
        JSON.stringify({ action: "load", xml, autosave: 1 }),
        "*"
      );
    });
    return unsubscribe;
  }, [subscribeRemoteXml]);

  return (
    <div style={{ flex: 1, minHeight: 0, position: "relative" }}>
      <iframe
        ref={iframeRef}
        src={buildEmbedUrl(viewingRevision)}
        title="Draw.io diagram editor"
        style={{ width: "100%", height: "100%", border: "none", display: "block" }}
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

