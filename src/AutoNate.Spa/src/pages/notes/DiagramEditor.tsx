import { useEffect, useMemo, useRef } from "react";
import { NoteDto } from "@/api/content";
import { useUpdateNote } from "@/hooks/useContent";
import { EditableNoteTitle } from "./EditableNoteTitle";
import { notesTheme } from "./notesTheme";

type Props = {
  note: NoteDto | null;
  noteName: string;
  // When set, the embed loads this historical revision's XML and runs in a
  // view-only flavor — autosave is disabled in the load action and any
  // incoming autosave/save events are ignored. versionNumber feeds the
  // iframe key so drawio remounts cleanly between revisions.
  revisionOverride?: {
    versionNumber: number;
    title: string | null;
    contentJsonb: string;
  } | null;
};

const AUTOSAVE_DEBOUNCE_MS = 600;

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
//   embed=1        | drives the embed protocol instead of standalone UI
//   ui=atlas       | compact toolbar layout suited for an inline editor
//   proto=json     | exchange messages as JSON instead of legacy XML
//   libraries=1    | enable the side-panel shape libraries
//   noSaveBtn=1    | hide the Save button; we autosave on every change
//   saveAndExit=0  | hide the "Save & exit" button too
//   spin=1         | show a spinner while the editor is initialising
// We point at `/drawio/index.html` instead of `/drawio/` because Vite's SPA
// fallback catches the directory-style URL and serves the AutoNate SPA shell
// (which then auth-redirects us). Explicit filename → Vite's static-asset
// middleware matches first and serves drawio's own index.html out of public/.
const EMBED_URL =
  `/drawio/index.html?` +
  new URLSearchParams({
    embed: "1",
    ui: "atlas",
    proto: "json",
    libraries: "1",
    noSaveBtn: "1",
    saveAndExit: "0",
    spin: "1"
  }).toString();

// Diagram editor backed by the diagrams.net (draw.io) iframe embed. The XML
// mxGraphModel is the canonical save format; we wrap it in JSONB so the
// notes.content_jsonb column shape stays uniform with the other note kinds.
//
// Communication is purely via postMessage:
//   draw.io → parent  { event: "init" | "autosave" | "save" | ... }
//   parent  → draw.io { action: "load", xml: "...", autosave: 1 }
//
// On `init` we push the existing XML in AND set `autosave: 1` so drawio
// starts emitting autosave events on every graph CHANGE. On `autosave`
// we debounce-write back to our backend.
export function DiagramEditor({ note, noteName, revisionOverride }: Props) {
  const viewingRevision = revisionOverride != null;
  const effectiveTitle = revisionOverride?.title ?? note?.title ?? noteName;
  const updateNote = useUpdateNote(note?.pageId ?? null);
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lastSavedRef = useRef<string | null>(null);
  const pendingXmlRef = useRef<string | null>(null);
  const noteIdRef = useRef<string | null>(null);
  const iframeRef = useRef<HTMLIFrameElement | null>(null);

  const initialXml = useMemo(
    () => parseXml(revisionOverride?.contentJsonb ?? note?.contentJsonb),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [note?.id, revisionOverride?.versionNumber ?? null]
  );

  // Reset the saved-content tracker when switching notes so an unrelated
  // autosave can't false-skip against another note's payload.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    lastSavedRef.current = note?.contentJsonb ?? null;
    pendingXmlRef.current = null;
    noteIdRef.current = note?.id ?? null;
  }, [note?.id]);

  // Flush any pending save on unmount.
  useEffect(() => {
    return () => {
      if (saveTimer.current) clearTimeout(saveTimer.current);
    };
  }, []);

  // Flush pending debounced save when the tab is about to unload. The
  // 600ms debounce window means a quick edit-then-refresh would otherwise
  // lose the last keystrokes; fetch+keepalive makes the browser send the
  // PATCH even as the page unloads. pagehide fires more reliably than
  // beforeunload (especially on mobile / bfcache transitions).
  useEffect(() => {
    const onPageHide = () => {
      const xml = pendingXmlRef.current;
      const id = noteIdRef.current;
      if (!xml || !id) return;
      const payload = { type: "drawio", version: 1, xml };
      const body = JSON.stringify({ contentJsonb: JSON.stringify(payload) });
      try {
        fetch(`/api/content/notes/${id}`, {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          credentials: "include",
          body,
          keepalive: true
        }).catch(() => {});
      } catch {
        // keepalive can throw on very large bodies; nothing more we can do
        // here. The autosave debounce will have caught most edits already.
      }
    };
    window.addEventListener("pagehide", onPageHide);
    return () => window.removeEventListener("pagehide", onPageHide);
  }, []);

  // postMessage listener for the iframe. Re-subscribed when the active note
  // changes so the `load` handler ships the correct initial XML.
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
          // Load the diagram. autosave is enabled only in the live editor —
          // for revision views we omit it so drawio doesn't fire change
          // events we'd have to suppress. The user can't accidentally save
          // a stale revision back to current.
          target.postMessage(
            JSON.stringify({
              action: "load",
              xml: initialXml ?? "",
              autosave: viewingRevision ? 0 : 1
            }),
            "*"
          );
          // We manage save state ourselves and ack autosave events back to
          // our backend, so drawio's internal beforeunload guard (which
          // shows "All changes will be lost" when its modified flag is on)
          // is redundant noise — disable it. Same-origin iframe, so direct
          // property assignment works. We re-null on each autosave too as
          // a belt-and-braces measure in case drawio reinstalls it.
          try {
            target.onbeforeunload = null;
          } catch {
            // cross-origin guard — shouldn't happen for /drawio/
          }
          break;
        }
        case "autosave":
        case "save": {
          if (!note || viewingRevision) return;
          try {
            target.onbeforeunload = null;
          } catch {
            // ignore
          }
          const xml = typeof data.xml === "string" ? data.xml : "";
          queueSave(xml);
          break;
        }
        default:
          // Other events (e.g. "exit", "exportComplete") are ignored —
          // we disabled the save/exit buttons so they shouldn't fire.
          break;
      }
    };
    window.addEventListener("message", onMessage);
    return () => window.removeEventListener("message", onMessage);
    // queueSave reads `note` via closure; rebind whenever the note or
    // revision changes (so the load action sends the right XML + flag).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [initialXml, note?.id, viewingRevision]);

  const queueSave = (xml: string) => {
    if (!note) return;
    pendingXmlRef.current = xml;
    if (saveTimer.current) clearTimeout(saveTimer.current);
    saveTimer.current = setTimeout(() => {
      const payload = { type: "drawio", version: 1, xml };
      const json = JSON.stringify(payload);
      if (json === lastSavedRef.current) {
        pendingXmlRef.current = null;
        return;
      }
      lastSavedRef.current = json;
      pendingXmlRef.current = null;
      updateNote.mutate({ id: note.id, body: { contentJsonb: json } });
    }, AUTOSAVE_DEBOUNCE_MS);
  };

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
            value={effectiveTitle}
            readOnly={viewingRevision || !note}
            onSave={(next) => {
              if (!note) return;
              updateNote.mutate({ id: note.id, body: { title: next } });
            }}
            style={{
              fontSize: 18,
              fontWeight: 700,
              color: notesTheme.dark
            }}
          />
        </div>
        <span style={savedStyle}>
          {updateNote.isPending ? (
            <>
              <i className="fa fa-cloud-arrow-up" style={{ marginRight: 5 }} />
              Saving…
            </>
          ) : (
            <>
              <i className="fa fa-check" style={{ marginRight: 5 }} />
              Auto-saved
            </>
          )}
        </span>
      </div>

      <div style={{ flex: 1, minHeight: 0, position: "relative" }}>
        {/* `key` forces a remount on note swap so the iframe reloads and
            draw.io re-sends `init`, prompting our listener to push the new
            note's XML. Without it, the iframe would keep the previous
            note's diagram on-screen until the user manually reloaded. */}
        <iframe
          key={`${note?.id ?? "empty"}:${revisionOverride?.versionNumber ?? "current"}`}
          ref={iframeRef}
          src={EMBED_URL}
          title="Draw.io diagram editor"
          style={{ width: "100%", height: "100%", border: "none", display: "block" }}
          // We intentionally don't set sandbox: the embed protocol relies on
          // postMessage from same-origin scripts inside the iframe, which
          // doesn't fit cleanly into a strict sandbox. embed.diagrams.net is
          // a trusted origin; the only thing it can interact with is the
          // message channel we explicitly listen to.
        />
      </div>
    </div>
  );
}

// Stored value shape: `{ "type": "drawio", "version": 1, "xml": "..." }`
// wrapped in JSONB. parseXml extracts the XML string, tolerating the empty
// "{}" placeholder a freshly-created note carries and the corner case of a
// raw XML string from a legacy save path.
function parseXml(raw: string | null | undefined): string | null {
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw);
    if (parsed && typeof parsed === "object" && typeof parsed.xml === "string") {
      return parsed.xml;
    }
  } catch {
    // Not JSON — accept raw XML as a fallback.
    if (raw.trimStart().startsWith("<")) return raw;
  }
  return null;
}

const savedStyle: React.CSSProperties = {
  fontSize: 11,
  color: notesTheme.muted,
  fontWeight: 600
};
