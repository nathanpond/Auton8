import { useState } from "react";
import type { PartialBlock } from "@blocknote/core";
import { useCreateBlockNote } from "@blocknote/react";
import { BlockNoteView } from "@blocknote/mantine";
import { ActionIcon, Tooltip } from "@mantine/core";
import { useUpdateNote } from "@/hooks/useContent";
import { NoteDto } from "@/api/content";
import { useYjsDocument } from "@/lib/yjs/useYjsDocument";
import { YjsEditor } from "@/lib/yjs/YjsEditor";
import { ConnectionStatusPill } from "@/lib/yjs/ConnectionStatusPill";
import { EditableNoteTitle } from "./EditableNoteTitle";
import { notesTheme } from "./notesTheme";

type Props = {
  note: NoteDto | null;
  noteName: string;
  // When set, renders this historical revision's content (read-only) instead
  // of the live note. Revision viewing bypasses Yjs entirely — a non-live
  // snapshot doesn't belong on the sync edge.
  revisionOverride?: {
    versionNumber: number;
    title: string | null;
    contentJsonb: string;
  } | null;
};

export function VisualTextEditor({ note, noteName, revisionOverride }: Props) {
  const viewingRevision = revisionOverride != null;
  const effectiveTitle = revisionOverride?.title ?? note?.title ?? noteName;
  const updateNote = useUpdateNote(note?.pageId ?? null);

  if (viewingRevision && revisionOverride) {
    return (
      <NotesEditorShell
        title={effectiveTitle}
        readOnlyTitle
        onTitleSave={() => {}}
        rightSlot={null}
      >
        <RevisionEditor
          // Re-mount on version swap so the new content goes through
          // useCreateBlockNote's `initialContent` cleanly.
          key={revisionOverride.versionNumber}
          rawContent={revisionOverride.contentJsonb}
        />
      </NotesEditorShell>
    );
  }

  if (!note) {
    return (
      <NotesEditorShell
        title={noteName}
        readOnlyTitle
        onTitleSave={() => {}}
        rightSlot={null}
      >
        <div style={{ color: notesTheme.muted, fontSize: 13 }}>
          Select a note to start editing.
        </div>
      </NotesEditorShell>
    );
  }

  return (
    <LiveNoteEditor
      // Re-mount on note swap so the Yjs handle teardown + recreate runs
      // through useEffect cleanly.
      key={note.id}
      note={note}
      title={effectiveTitle}
      onTitleSave={(next) => updateNote.mutate({ id: note.id, body: { title: next } })}
    />
  );
}

function LiveNoteEditor({
  note,
  title,
  onTitleSave
}: {
  note: NoteDto;
  title: string;
  onTitleSave: (next: string) => void;
}) {
  const { handle, status, role } = useYjsDocument(`note:${note.id}`);
  const [showSidebar, setShowSidebar] = useState(false);

  return (
    <NotesEditorShell
      title={title}
      readOnlyTitle={false}
      onTitleSave={onTitleSave}
      rightSlot={
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <ConnectionStatusPill status={status} role={role} />
          <Tooltip label={showSidebar ? "Hide comments" : "Show comments"}>
            <ActionIcon
              variant={showSidebar ? "filled" : "subtle"}
              color="gray"
              size="sm"
              onClick={() => setShowSidebar((v) => !v)}
              aria-label="Toggle threads sidebar"
            >
              <i className="fa fa-comments" />
            </ActionIcon>
          </Tooltip>
        </div>
      }
    >
      {handle ? (
        <YjsEditor
          handle={handle}
          editable={role === "editor"}
          showSidebar={showSidebar}
          role={role}
        />
      ) : null}
    </NotesEditorShell>
  );
}

function RevisionEditor({ rawContent }: { rawContent: string }) {
  const initialContent = parseInitialContent(rawContent);
  const editor = useCreateBlockNote({
    initialContent,
    placeholders: { default: "Type to start writing…" }
  });
  // Revisions are immutable historical snapshots; the editor is non-Yjs
  // and never editable.
  editor.isEditable = false;
  return <BlockNoteView editor={editor} editable={false} theme="light" />;
}

function NotesEditorShell({
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
        {rightSlot}
      </div>

      <div style={{ padding: "20px 0 20px 40px", flex: 1, overflowY: "auto" }}>
        <div style={{ width: "100%" }}>
          <EditableNoteTitle
            value={title}
            readOnly={readOnlyTitle}
            onSave={onTitleSave}
            style={{
              margin: "0 0 18px",
              fontSize: 28,
              fontWeight: 700,
              letterSpacing: "-0.02em",
              color: notesTheme.dark
            }}
          />
          {children}
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
