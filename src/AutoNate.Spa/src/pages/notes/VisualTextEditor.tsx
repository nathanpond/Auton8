import { useEffect, useRef } from "react";
import type { PartialBlock } from "@blocknote/core";
import { useCreateBlockNote } from "@blocknote/react";
import { BlockNoteView } from "@blocknote/mantine";
import { useUpdateNote } from "@/hooks/useContent";
import { NoteDto } from "@/api/content";
import { EditableNoteTitle } from "./EditableNoteTitle";
import { notesTheme } from "./notesTheme";

type Props = {
  note: NoteDto | null;
  noteName: string;
  // When set, renders this historical revision's content (read-only) instead
  // of the live note. versionNumber is part of the editor's recreate-key.
  revisionOverride?: {
    versionNumber: number;
    title: string | null;
    contentJsonb: string;
  } | null;
};

const AUTOSAVE_DEBOUNCE_MS = 600;

export function VisualTextEditor({ note, noteName, revisionOverride }: Props) {
  const viewingRevision = revisionOverride != null;
  const effectiveContent = revisionOverride?.contentJsonb ?? note?.contentJsonb;
  const effectiveTitle = revisionOverride?.title ?? note?.title ?? noteName;
  const updateNote = useUpdateNote(note?.pageId ?? null);
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lastSavedRef = useRef<string | null>(null);

  const editor = useCreateBlockNote({
    initialContent: parseInitialContent(effectiveContent),
    placeholders: { default: "Type to start writing…" }
  });

  // Reset autosave bookkeeping when the note (or revision) swaps. Without
  // this, an autosave on a fresh note could compare against the previous
  // note's saved-JSON ref and skip a real first save.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    lastSavedRef.current = effectiveContent ?? null;
  }, [note?.id, revisionOverride?.versionNumber ?? null]);

  // Mirror the editable flag onto the editor instance. BlockNoteView's
  // `editable` prop controls the view; setting the property keeps the
  // editor's own command surface aligned too.
  useEffect(() => {
    editor.isEditable = !viewingRevision;
  }, [editor, viewingRevision]);

  // Subscribe to changes for debounced autosave. onChange returns a cleanup
  // function that detaches the listener — important to avoid double-saves
  // across re-mounts.
  useEffect(() => {
    if (viewingRevision || !note) return;
    const unsubscribe = editor.onChange((ed) => {
      if (saveTimer.current) clearTimeout(saveTimer.current);
      saveTimer.current = setTimeout(() => {
        const json = JSON.stringify(ed.document);
        if (json === lastSavedRef.current) return;
        lastSavedRef.current = json;
        updateNote.mutate({ id: note.id, body: { contentJsonb: json } });
      }, AUTOSAVE_DEBOUNCE_MS);
    });
    return unsubscribe;
  }, [editor, note, viewingRevision, updateNote]);

  // Flush any pending save on unmount so we don't lose the last keystroke.
  useEffect(() => {
    return () => {
      if (saveTimer.current) clearTimeout(saveTimer.current);
    };
  }, []);

  return (
    <div
      className="notes-editor-bleed"
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
          justifyContent: "flex-end",
          padding: "6px 14px",
          borderBottom: `1px solid ${notesTheme.border}`,
          minHeight: 32
        }}
      >
        {updateNote.isPending ? (
          <span style={savedStyle}>
            <i className="fa fa-cloud-arrow-up" style={{ marginRight: 5 }} />
            Saving…
          </span>
        ) : (
          <span style={savedStyle}>
            <i className="fa fa-check" style={{ marginRight: 5 }} />
            Auto-saved
          </span>
        )}
      </div>

      <div style={{ padding: "20px 0 20px 40px", flex: 1, overflowY: "auto" }}>
        <div style={{ width: "100%" }}>
          <EditableNoteTitle
            value={effectiveTitle}
            readOnly={viewingRevision || !note}
            onSave={(next) => {
              if (!note) return;
              updateNote.mutate({ id: note.id, body: { title: next } });
            }}
            style={{
              margin: "0 0 18px",
              fontSize: 28,
              fontWeight: 700,
              letterSpacing: "-0.02em",
              color: notesTheme.dark
            }}
          />
          <BlockNoteView editor={editor} editable={!viewingRevision} theme="light" />
        </div>
      </div>
    </div>
  );
}

function parseInitialContent(raw: string | null | undefined): PartialBlock[] | undefined {
  if (!raw) return undefined;
  try {
    const parsed = JSON.parse(raw);
    if (Array.isArray(parsed) && parsed.length > 0) {
      return parsed as PartialBlock[];
    }
    return undefined;
  } catch {
    return undefined;
  }
}

const savedStyle: React.CSSProperties = {
  fontSize: 11,
  color: notesTheme.muted,
  fontWeight: 600
};
