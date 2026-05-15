import { Suspense, lazy, useEffect, useMemo, useRef } from "react";
import type { ExcalidrawImperativeAPI, ExcalidrawInitialDataState } from "@excalidraw/excalidraw/types";
import { NoteDto } from "@/api/content";
import { useUpdateNote } from "@/hooks/useContent";
import { EditableNoteTitle } from "./EditableNoteTitle";
import { notesTheme } from "./notesTheme";

// Excalidraw ships ~1MB of CSS+JS. Code-split via React.lazy so the chunk only
// loads when a drawing note tab is actually opened.
const Excalidraw = lazy(() =>
  import("@excalidraw/excalidraw").then((mod) => ({ default: mod.Excalidraw }))
);

type Props = {
  note: NoteDto | null;
  noteName: string;
  // When set, the canvas displays this revision's scene in view-only mode.
  // versionNumber feeds the iframe key so Excalidraw remounts cleanly when
  // the user navigates between revisions.
  revisionOverride?: {
    versionNumber: number;
    title: string | null;
    contentJsonb: string;
  } | null;
};

const AUTOSAVE_DEBOUNCE_MS = 600;

// Napkin = Excalidraw-backed sketch surface. The drawing scene is persisted
// as JSON (Excalidraw's standard local-export shape) to notes.content_jsonb
// and round-trips through the same debounced-autosave pattern as the rich-
// text and page editors.
export function NapkinEditor({ note, noteName, revisionOverride }: Props) {
  const viewingRevision = revisionOverride != null;
  const effectiveTitle = revisionOverride?.title ?? note?.title ?? noteName;
  const updateNote = useUpdateNote(note?.pageId ?? null);
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lastSavedRef = useRef<string | null>(null);
  const apiRef = useRef<ExcalidrawImperativeAPI | null>(null);

  // Recompute initialData when the note swaps OR when navigating between
  // revisions so the Excalidraw instance we mount can't show a stale scene.
  const initialData = useMemo<ExcalidrawInitialDataState | null>(
    () => parseScene(revisionOverride?.contentJsonb ?? note?.contentJsonb),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [note?.id, revisionOverride?.versionNumber ?? null]
  );

  // Reset the saved-content tracker when switching notes so an unrelated
  // autosave can't false-skip against another note's payload.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    lastSavedRef.current = note?.contentJsonb ?? null;
  }, [note?.id]);

  // Flush any pending save on unmount.
  useEffect(() => {
    return () => {
      if (saveTimer.current) clearTimeout(saveTimer.current);
    };
  }, []);

  const queueSave = (elements: readonly unknown[], appState: unknown) => {
    if (!note || viewingRevision) return;
    if (saveTimer.current) clearTimeout(saveTimer.current);
    saveTimer.current = setTimeout(() => {
      // Excalidraw's appState is huge and includes ephemeral fields (cursor
      // positions, collaborators, etc). Keep only the bits that matter for
      // re-opening the document: the canvas chrome the user chose.
      const slimAppState = pickPersistedAppState(appState);
      const payload = {
        type: "excalidraw",
        version: 2,
        source: "autonate",
        elements,
        appState: slimAppState
      };
      const json = JSON.stringify(payload);
      if (json === lastSavedRef.current) return;
      lastSavedRef.current = json;
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
        <Suspense
          fallback={
            <div
              style={{
                position: "absolute",
                inset: 0,
                display: "grid",
                placeItems: "center",
                color: notesTheme.muted,
                fontSize: 12
              }}
            >
              <span>
                <i className="fa fa-spinner fa-spin" style={{ marginRight: 6 }} />
                Loading drawing surface…
              </span>
            </div>
          }
        >
          {/* `key` forces a remount on note swap so Excalidraw re-applies the
              new initialData. Without it Excalidraw treats `initialData` as
              mount-time only and our scene wouldn't change when the active
              note changes. */}
          <Excalidraw
            key={`${note?.id ?? "empty"}:${revisionOverride?.versionNumber ?? "current"}`}
            initialData={initialData}
            viewModeEnabled={viewingRevision}
            excalidrawAPI={(api) => {
              apiRef.current = api;
            }}
            onChange={queueSave}
          />
        </Suspense>
      </div>
    </div>
  );
}

function parseScene(raw: string | null | undefined): ExcalidrawInitialDataState | null {
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw);
    if (parsed && typeof parsed === "object") {
      // Tolerate the "{}" placeholder that newly-created notes get on the
      // server before any draw event has fired.
      if (Array.isArray(parsed.elements) || parsed.appState) {
        return {
          elements: parsed.elements ?? [],
          appState: parsed.appState ?? {},
          files: parsed.files ?? {}
        };
      }
    }
    return null;
  } catch {
    return null;
  }
}

// Strip ephemeral / non-serializable fields from Excalidraw's appState before
// persisting. Excalidraw's own serializeAsJSON does similar pruning under the
// hood; we open-code a small subset because importing the helper would mean
// the chunk loads on app boot instead of lazily inside Suspense.
function pickPersistedAppState(raw: unknown): Record<string, unknown> {
  if (!raw || typeof raw !== "object") return {};
  const state = raw as Record<string, unknown>;
  const keep: (keyof typeof state)[] = [
    "viewBackgroundColor",
    "gridSize",
    "gridModeEnabled",
    "theme",
    "currentItemStrokeColor",
    "currentItemBackgroundColor",
    "currentItemFillStyle",
    "currentItemStrokeWidth",
    "currentItemStrokeStyle",
    "currentItemRoughness",
    "currentItemOpacity",
    "currentItemFontFamily",
    "currentItemFontSize",
    "currentItemTextAlign",
    "currentItemStartArrowhead",
    "currentItemEndArrowhead",
    "scrollX",
    "scrollY",
    "zoom"
  ];
  const out: Record<string, unknown> = {};
  for (const key of keep) {
    if (key in state) out[key] = state[key];
  }
  return out;
}

const savedStyle: React.CSSProperties = {
  fontSize: 11,
  color: notesTheme.muted,
  fontWeight: 600
};
