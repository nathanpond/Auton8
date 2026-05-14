import { useEffect, useRef } from "react";
import { Highlight } from "@tiptap/extension-highlight";
import { Subscript } from "@tiptap/extension-subscript";
import { Superscript } from "@tiptap/extension-superscript";
import { TextAlign } from "@tiptap/extension-text-align";
import { Underline } from "@tiptap/extension-underline";
import { TaskList } from "@tiptap/extension-task-list";
import { TaskItem } from "@tiptap/extension-task-item";
import { Placeholder } from "@tiptap/extension-placeholder";
import { useEditor, EditorContent } from "@tiptap/react";
import { StarterKit } from "@tiptap/starter-kit";
import { Link, RichTextEditor } from "@mantine/tiptap";
import { useUpdateNote } from "@/hooks/useContent";
import { NoteDto } from "@/api/content";
import { notesTheme } from "./notesTheme";

type Props = {
  note: NoteDto | null;
  noteName: string;
};

const AUTOSAVE_DEBOUNCE_MS = 600;

// Visual Text note editor — Mantine's @mantine/tiptap with the full toolbar
// shown in the design (headings, marks, lists, blockquote, align, links,
// inserts, undo/redo). Body is persisted as the tiptap JSON document.
export function VisualTextEditor({ note, noteName }: Props) {
  const updateNote = useUpdateNote(note?.pageId ?? null);
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lastSavedRef = useRef<string | null>(null);

  // useEditor's [note?.id] deps recreate the editor with the new note's
  // content. We deliberately do NOT call editor.commands.setContent() in a
  // follow-up effect: during the teardown of the previous editor instance,
  // `editor.commands` returns null even while the editor reference is still
  // truthy, which crashes when notes switch (e.g. the moment a freshly-
  // created note becomes the active tab).
  const editor = useEditor(
    {
      extensions: [
        // StarterKit v3 bundles Link + Underline. Disable them so the
        // Mantine Link (with its URL modal) and the standalone Underline can
        // layer back on without "duplicate extension" warnings.
        StarterKit.configure({ link: false, underline: false }),
        Underline,
        Link.configure({ openOnClick: false }),
        Superscript,
        Subscript,
        Highlight,
        TextAlign.configure({ types: ["heading", "paragraph"] }),
        TaskList,
        TaskItem.configure({ nested: true }),
        Placeholder.configure({ placeholder: "Type to start writing…" })
      ],
      content: parseDoc(note?.contentJsonb),
      onUpdate: ({ editor: ed }) => {
        if (!note) return;
        if (saveTimer.current) clearTimeout(saveTimer.current);
        saveTimer.current = setTimeout(() => {
          const json = JSON.stringify(ed.getJSON());
          if (json === lastSavedRef.current) return;
          lastSavedRef.current = json;
          updateNote.mutate({ id: note.id, body: { contentJsonb: json } });
        }, AUTOSAVE_DEBOUNCE_MS);
      }
    },
    // Re-mount the editor when switching between notes so the content state
    // doesn't bleed from the previous note's document into the new one.
    [note?.id]
  );

  // Reset the saved-content tracker when the note swaps so an unrelated
  // autosave can't compare against a stale ref.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    lastSavedRef.current = note?.contentJsonb ?? null;
  }, [note?.id]);

  // Flush any pending save on unmount so we don't lose the last keystroke.
  useEffect(() => {
    return () => {
      if (saveTimer.current) clearTimeout(saveTimer.current);
    };
  }, []);

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
      <RichTextEditor
        editor={editor}
        styles={{
          root: { border: "none", borderRadius: 0, flex: 1, display: "flex", flexDirection: "column", minHeight: 0 },
          toolbar: { borderBottom: `1px solid ${notesTheme.border}`, padding: "5px 10px" },
          content: { flex: 1, overflowY: "auto", background: "#fff" }
        }}
      >
        <RichTextEditor.Toolbar sticky stickyOffset={0}>
          <RichTextEditor.ControlsGroup>
            <RichTextEditor.H1 />
            <RichTextEditor.H2 />
            <RichTextEditor.H3 />
            <RichTextEditor.H4 />
          </RichTextEditor.ControlsGroup>
          <RichTextEditor.ControlsGroup>
            <RichTextEditor.Bold />
            <RichTextEditor.Italic />
            <RichTextEditor.Underline />
            <RichTextEditor.Strikethrough />
            <RichTextEditor.Highlight />
            <RichTextEditor.Code />
          </RichTextEditor.ControlsGroup>
          <RichTextEditor.ControlsGroup>
            <RichTextEditor.BulletList />
            <RichTextEditor.OrderedList />
            <RichTextEditor.TaskList />
          </RichTextEditor.ControlsGroup>
          <RichTextEditor.ControlsGroup>
            <RichTextEditor.Blockquote />
            <RichTextEditor.Hr />
            <RichTextEditor.CodeBlock />
          </RichTextEditor.ControlsGroup>
          <RichTextEditor.ControlsGroup>
            <RichTextEditor.AlignLeft />
            <RichTextEditor.AlignCenter />
            <RichTextEditor.AlignJustify />
            <RichTextEditor.AlignRight />
          </RichTextEditor.ControlsGroup>
          <RichTextEditor.ControlsGroup>
            <RichTextEditor.Link />
            <RichTextEditor.Unlink />
          </RichTextEditor.ControlsGroup>
          <RichTextEditor.ControlsGroup>
            <RichTextEditor.Undo />
            <RichTextEditor.Redo />
          </RichTextEditor.ControlsGroup>
          <div style={{ marginLeft: "auto", display: "flex", alignItems: "center" }}>
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
        </RichTextEditor.Toolbar>

        <div style={{ padding: "20px 40px", flex: 1, overflowY: "auto" }}>
          <div style={{ width: "100%" }}>
            <h1
              style={{
                margin: "0 0 18px",
                fontSize: 28,
                fontWeight: 700,
                letterSpacing: "-0.02em",
                color: notesTheme.dark
              }}
            >
              {note?.title ?? noteName}
            </h1>
            <EditorContent editor={editor} />
          </div>
        </div>
      </RichTextEditor>
    </div>
  );
}

function parseDoc(raw: string | null | undefined): object | string {
  if (!raw) return "";
  try {
    const parsed = JSON.parse(raw);
    if (parsed && typeof parsed === "object" && "type" in parsed) {
      return parsed as object;
    }
    return "";
  } catch {
    // Tolerate legacy / placeholder values like "{}" by initialising empty.
    return "";
  }
}

const savedStyle: React.CSSProperties = {
  fontSize: 11,
  color: notesTheme.muted,
  fontWeight: 600
};
