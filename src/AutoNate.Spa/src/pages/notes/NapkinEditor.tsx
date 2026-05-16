import { Suspense, lazy, useMemo, useState } from "react";
import type {
  ExcalidrawImperativeAPI,
  ExcalidrawInitialDataState
} from "@excalidraw/excalidraw/types";
import { NoteDto } from "@/api/content";
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
      {handle ? <CollabScene handle={handle} viewer={role === "viewer"} /> : null}
    </NapkinShell>
  );
}

function CollabScene({
  handle,
  viewer
}: {
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

  return (
    <ExcalidrawCanvas
      initialData={initialData}
      onChange={(elements, appState, files) =>
        onChange(elements as readonly { id: string; version?: number }[], appState, files)
      }
      onApiReady={setApi}
      onPointerUpdate={onPointerUpdate}
      // Viewer connections render the canvas non-editable. The server-side
      // readOnly enforcement is the actual security boundary — this just
      // hides the editing tools so users don't try.
      viewModeEnabled={viewer}
    />
  );
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
