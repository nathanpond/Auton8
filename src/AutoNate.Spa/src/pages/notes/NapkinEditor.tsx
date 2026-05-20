import { Suspense, lazy, useEffect, useMemo, useRef, useState } from "react";
import type {
  ExcalidrawImperativeAPI,
  ExcalidrawInitialDataState
} from "@excalidraw/excalidraw/types";
import { NoteDto, updateNote as patchNote } from "@/api/content";
import { useUpdateNote } from "@/hooks/useContent";
import { useMe } from "@/hooks/useMe";
import { useYjsDocument } from "@/lib/yjs/useYjsDocument";
import { useYjsExcalidraw } from "@/lib/yjs/useYjsExcalidraw";
import { useExcalidrawAwareness } from "@/lib/yjs/useExcalidrawAwareness";
import { ConnectionStatusPill } from "@/lib/yjs/ConnectionStatusPill";
import { userCursorColor } from "@/lib/yjs/userColor";
import { EditableNoteTitle } from "./EditableNoteTitle";
import { notesTheme } from "./notesTheme";

// Excalidraw ships ~1MB of CSS+JS. Code-split via React.lazy so the chunk
// only loads when a drawing note tab is actually opened.
const Excalidraw = lazy(() =>
  import("@excalidraw/excalidraw").then((mod) => ({ default: mod.Excalidraw }))
);

type Props = {
  note: NoteDto | null;
  noteName: string;
  // When set, the canvas displays this revision's scene in view-only mode.
  // versionNumber feeds Excalidraw's key so it remounts cleanly when the
  // user navigates between revisions. Revisions bypass Yjs entirely — they
  // are non-live historical snapshots.
  revisionOverride?: {
    versionNumber: number;
    title: string | null;
    contentJsonb: string;
  } | null;
};

// Napkin = Excalidraw-backed sketch surface. Phase 4 puts the scene under
// Yjs collab: elements live in a Y.Array<Y.Map> and the persisted appState
// in a Y.Map; Hocuspocus syncs the lot. The previous autosave-by-debounce
// path is gone — durability is owned by Hocuspocus (server) + y-indexeddb
// (local). What remains here is the editor chrome and the revision-view
// branch that bypasses Yjs.
// Debounce window for the SVG snapshot. Tuned to "land within a couple of
// seconds of the user stopping editing" — short enough that page-embed
// readers see fresh previews quickly, long enough that mid-stroke onChange
// bursts collapse into one export + one PATCH.
const SVG_SNAPSHOT_DEBOUNCE_MS = 1500;

export function NapkinEditor({ note, noteName, revisionOverride }: Props) {
  const viewingRevision = revisionOverride != null;
  const effectiveTitle = revisionOverride?.title ?? note?.title ?? noteName;
  const updateNote = useUpdateNote(note?.pageId ?? null);

  if (viewingRevision && revisionOverride) {
    return (
      <NapkinShell
        title={effectiveTitle}
        readOnlyTitle
        onTitleSave={() => {}}
        rightSlot={null}
      >
        <RevisionScene
          // Re-mount on version swap so Excalidraw re-applies the new
          // initialData (it only reads it at mount).
          key={revisionOverride.versionNumber}
          rawContent={revisionOverride.contentJsonb}
        />
      </NapkinShell>
    );
  }

  if (!note) {
    return (
      <NapkinShell
        title={noteName}
        readOnlyTitle
        onTitleSave={() => {}}
        rightSlot={null}
      >
        <div style={{ color: notesTheme.muted, fontSize: 13, padding: 24 }}>
          Select a note to start drawing.
        </div>
      </NapkinShell>
    );
  }

  return (
    <LiveNapkin
      // Re-mount on note swap so the Yjs handle teardown + recreate runs
      // through useEffect cleanly.
      key={note.id}
      note={note}
      title={effectiveTitle}
      onTitleSave={(next) => updateNote.mutate({ id: note.id, body: { title: next } })}
    />
  );
}

function LiveNapkin({
  note,
  title,
  onTitleSave
}: {
  note: NoteDto;
  title: string;
  onTitleSave: (next: string) => void;
}) {
  const { handle, status, role } = useYjsDocument(`napkin:${note.id}`);

  return (
    <NapkinShell
      title={title}
      readOnlyTitle={false}
      onTitleSave={onTitleSave}
      rightSlot={<ConnectionStatusPill status={status} role={role} />}
    >
      {handle ? (
        <CollabScene note={note} handle={handle} viewer={role === "viewer"} />
      ) : null}
    </NapkinShell>
  );
}

function CollabScene({
  note,
  handle,
  viewer
}: {
  note: NoteDto;
  handle: NonNullable<ReturnType<typeof useYjsDocument>["handle"]>;
  viewer: boolean;
}) {
  // useState (not useRef) so the hooks below re-run their effects once
  // Excalidraw hands us its imperative API. With a ref, the api change
  // wouldn't trigger a re-render and the observers / awareness
  // subscribers would never see the non-null api.
  const [api, setApi] = useState<ExcalidrawImperativeAPI | null>(null);

  const me = useMe();
  const currentUser = me.data?.authenticated
    ? {
        id: me.data.userId,
        displayName:
          [me.data.firstName, me.data.lastName].filter(Boolean).join(" ") ||
          me.data.username,
        color: userCursorColor(me.data.userId)
      }
    : {
        id: "anonymous",
        displayName: "Anonymous",
        color: userCursorColor("anonymous")
      };

  const { initialData, onChange } = useYjsExcalidraw({
    doc: handle.doc,
    provider: handle.provider,
    excalidrawAPI: api
  });
  // Live cursors: broadcast our pointer state through Yjs awareness;
  // remote pointers are pushed into Excalidraw's collaborators Map via
  // updateScene.
  const { onPointerUpdate } = useExcalidrawAwareness({
    provider: handle.provider,
    excalidrawAPI: api,
    currentUser
  });

  const scheduleSnapshot = useSvgSnapshotScheduler({
    noteId: note.id,
    excalidrawAPI: api,
    enabled: !viewer
  });

  return (
    <ExcalidrawCanvas
      initialData={initialData}
      onChange={(elements, appState, files) => {
        onChange(elements as readonly { id: string; version?: number }[], appState, files);
        scheduleSnapshot();
      }}
      onApiReady={setApi}
      onPointerUpdate={onPointerUpdate}
      // Viewer connections render the canvas non-editable. The server-side
      // readOnly enforcement is the actual security boundary — this just
      // hides the editing tools so users don't try.
      viewModeEnabled={viewer}
    />
  );
}

// Debounced SVG-snapshot scheduler for drawing notes. Each Excalidraw
// onChange resets the timer; when it fires we call exportToSvg against the
// current scene and PATCH the note's previewSvg. The snapshot is what the
// page-embed renderer reads in view mode — keeping it out of Yjs avoids
// bloating the CRDT update log with full-text SVG rewrites per save.
//
// Deliberately bypasses `useUpdateNote` so the PATCH does NOT invalidate
// the notes query. If it did, the cascade (refetch → new NoteDto ref →
// new `note` prop into CollabScene → new onChange closure into Excalidraw)
// would churn the Excalidraw component while the user is mid-stroke and
// before Hocuspocus has debounced its store, with the failure mode being
// "user refreshes shortly after drawing and the drawing is gone." We
// don't need the cache to refresh on snapshot — the previewSvg is only
// read by page-embed renderers, which fetch their own notes data the
// next time they mount.
function useSvgSnapshotScheduler(args: {
  noteId: string;
  excalidrawAPI: ExcalidrawImperativeAPI | null;
  enabled: boolean;
}): () => void {
  const { noteId, excalidrawAPI, enabled } = args;
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lastSentRef = useRef<string | null>(null);
  // Read excalidrawAPI through a ref so the returned scheduler function
  // can be stable — Excalidraw's onChange prop identity should NOT churn
  // every parent render.
  const apiRef = useRef(excalidrawAPI);
  apiRef.current = excalidrawAPI;
  const enabledRef = useRef(enabled);
  enabledRef.current = enabled;
  const noteIdRef = useRef(noteId);
  noteIdRef.current = noteId;

  // Clear pending timer on unmount so a tab swap can't fire a snapshot
  // against the previous note's API.
  useEffect(() => {
    return () => {
      if (timer.current) clearTimeout(timer.current);
    };
  }, []);

  return useStableCallback(() => {
    if (!enabledRef.current || !apiRef.current) return;
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(async () => {
      timer.current = null;
      const api = apiRef.current;
      if (!api) return;
      try {
        const elements = api.getSceneElements();
        if (elements.length === 0) {
          // Skip empty scenes — sending a tiny empty SVG would just blow
          // away the last meaningful snapshot if the user transiently
          // selects-all-and-deletes.
          return;
        }
        const appState = api.getAppState();
        const files = api.getFiles();
        // Dynamic import: @excalidraw/excalidraw is already lazy-loaded by
        // the canvas above, so this resolves from the same chunk without
        // triggering a fresh network fetch.
        const { exportToSvg } = await import("@excalidraw/excalidraw");
        const svgEl = await exportToSvg({
          elements,
          appState: { ...appState, exportBackground: true, exportWithDarkMode: false },
          files,
          exportPadding: 8
        });
        const previewSvg = new XMLSerializer().serializeToString(svgEl);
        if (previewSvg === lastSentRef.current) return;
        lastSentRef.current = previewSvg;
        // Fire-and-forget: no cache invalidation, no React re-render.
        void patchNote(noteIdRef.current, { previewSvg }).catch((err) => {
          console.warn("[NapkinEditor] previewSvg PATCH failed:", err);
        });
      } catch (err) {
        console.warn("[NapkinEditor] preview SVG snapshot failed:", err);
      }
    }, SVG_SNAPSHOT_DEBOUNCE_MS);
  });
}

// Tiny ref-backed stable callback. Returns a function whose identity is
// fixed across renders but always invokes the latest body. Avoids pulling
// in a useEvent shim just for this one use.
function useStableCallback<T extends (...args: never[]) => unknown>(fn: T): T {
  const ref = useRef(fn);
  ref.current = fn;
  const stable = useRef<T>(
    ((...args: never[]) => ref.current(...args)) as T
  );
  return stable.current;
}

function RevisionScene({ rawContent }: { rawContent: string }) {
  const initialData = useMemo(() => parseScene(rawContent), [rawContent]);
  return (
    <ExcalidrawCanvas
      initialData={initialData}
      onChange={() => {}}
      onApiReady={() => {}}
      viewModeEnabled
    />
  );
}

function ExcalidrawCanvas({
  initialData,
  onChange,
  onApiReady,
  onPointerUpdate,
  viewModeEnabled
}: {
  initialData: ExcalidrawInitialDataState | null;
  // Excalidraw types `onChange` with the strict element discriminated
  // union; we treat elements opaquely in our binding, so accept the
  // strict shape and forward it.
  onChange: (
    elements: NonNullable<ExcalidrawInitialDataState["elements"]>,
    appState: unknown,
    files: unknown
  ) => void;
  onApiReady: (api: ExcalidrawImperativeAPI) => void;
  // Optional — only the live (collab) editor wires this; revision
  // views pass undefined so Excalidraw skips broadcasting pointer
  // moves through awareness.
  onPointerUpdate?: (payload: {
    pointer: { x: number; y: number; tool: "pointer" | "laser" };
    button: "down" | "up";
  }) => void;
  viewModeEnabled: boolean;
}) {
  return (
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
        <Excalidraw
          initialData={initialData}
          viewModeEnabled={viewModeEnabled}
          excalidrawAPI={onApiReady}
          onChange={onChange}
          onPointerUpdate={onPointerUpdate}
          // `isCollaborating` flips Excalidraw into collab mode so the
          // collaborators Map (set via updateScene) actually renders.
          // Without this Excalidraw treats the doc as solo-edit.
          isCollaborating={Boolean(onPointerUpdate)}
        />
      </Suspense>
    </div>
  );
}

function NapkinShell({
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

// Used only by RevisionScene to bootstrap a read-only historical view from
// the stored snapshot. The live editor's initialData comes from the Y.Doc
// (via useYjsExcalidraw).
function parseScene(raw: string | null | undefined): ExcalidrawInitialDataState | null {
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw);
    if (parsed && typeof parsed === "object") {
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
