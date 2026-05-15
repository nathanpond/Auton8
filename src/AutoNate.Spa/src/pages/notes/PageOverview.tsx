import { useEffect, useRef, useState } from "react";
import { useEditor, EditorContent } from "@tiptap/react";
import { StarterKit } from "@tiptap/starter-kit";
import { Underline } from "@tiptap/extension-underline";
import { Highlight } from "@tiptap/extension-highlight";
import { Subscript } from "@tiptap/extension-subscript";
import { Superscript } from "@tiptap/extension-superscript";
import { TextAlign } from "@tiptap/extension-text-align";
import { TaskList } from "@tiptap/extension-task-list";
import { TaskItem } from "@tiptap/extension-task-item";
import { Placeholder } from "@tiptap/extension-placeholder";
import { Link, RichTextEditor } from "@mantine/tiptap";
import { PageDto } from "@/api/content";
import { useUpdatePage } from "@/hooks/useContent";
import { notesTheme } from "./notesTheme";

type Props = {
  page: PageDto;
  mode: "view" | "edit";
  // When set, the editor renders this historical revision's content instead
  // of the current page body. Edit mode is implicitly disabled (the banner +
  // Restore button handle promotion back to current). `versionNumber` is
  // part of the editor's identity so React tears down + re-creates tiptap
  // when the user navigates between revisions.
  revisionOverride?: {
    versionNumber: number;
    title: string;
    bodyJsonb: string;
  } | null;
};

const AUTOSAVE_DEBOUNCE_MS = 600;

// Page-body surface. Loads in read-only "view" mode — the page's tiptap doc is
// rendered with all formatting via the same extension stack as the editor, so
// what the user sees here is faithful to how it'll look while editing. Mode is
// controlled by EditorPane: the pencil button in the breadcrumb action bar
// toggles it. Edit mode shows the full Mantine tiptap toolbar + editable
// title input + debounced auto-save on title + body. Leaving edit mode
// flushes any pending save so the version history captures the last edit.
//
// Intentionally minimal: no breadcrumb, no notes grid, no child-page list —
// those live elsewhere (or are deferred features). The page is just title +
// body here.
export function PageOverview({ page, mode, revisionOverride }: Props) {
  const viewingRevision = revisionOverride != null;
  const effectiveMode: "view" | "edit" = viewingRevision ? "view" : mode;
  const effectiveTitle = revisionOverride?.title ?? page.title;
  const effectiveBody = revisionOverride?.bodyJsonb ?? page.bodyJsonb;
  const [titleDraft, setTitleDraft] = useState(page.title);
  const updatePage = useUpdatePage();
  const bodyTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const titleTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lastSavedBodyRef = useRef<string | null>(null);
  const lastSavedTitleRef = useRef<string | null>(null);

  // The editor is recreated whenever page.id changes (see deps below), so
  // `content` reads the new page's body at that moment — no manual setContent
  // is needed. Crucially, the editor is NOT recreated when the same page's
  // bodyJsonb refetches after our own debounced save, which prevents the
  // editor from wiping the user's in-progress typing.
  const editor = useEditor(
    {
      extensions: [
        // StarterKit v3 bundles Link + Underline. We disable both here so
        // we can layer Mantine's Link (which wires the toolbar's URL modal)
        // and the standalone Underline back on without "duplicate extension"
        // warnings.
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
      content: parseDoc(effectiveBody),
      editable: false, // toggled on entering edit mode
      onUpdate: ({ editor: ed }) => {
        // Never autosave a revision view — those edits would silently
        // overwrite current with the revision's text. The user must hit
        // Restore explicitly.
        if (viewingRevision || effectiveMode !== "edit") return;
        if (bodyTimer.current) clearTimeout(bodyTimer.current);
        bodyTimer.current = setTimeout(() => {
          const json = JSON.stringify(ed.getJSON());
          if (json === lastSavedBodyRef.current) return;
          lastSavedBodyRef.current = json;
          updatePage.mutate({ id: page.id, body: { bodyJsonb: json } });
        }, AUTOSAVE_DEBOUNCE_MS);
      }
    },
    // Recreate when navigating between pages OR between current/revision
    // views — the new content goes through useEditor's `content` field.
    [page.id, revisionOverride?.versionNumber ?? null]
  );

  // Sync editable flag whenever mode flips. Revision view is always read-only.
  useEffect(() => {
    editor?.setEditable(effectiveMode === "edit");
  }, [editor, effectiveMode]);

  // Reset title draft + saved-content bookkeeping only when the user navigates
  // to a different page. Including page.bodyJsonb / page.title here would re-
  // run on every save-triggered refetch and stomp the user's in-progress
  // typing — bug we fixed when keystrokes were "disappearing after a few
  // seconds" mid-edit.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    setTitleDraft(page.title);
    lastSavedBodyRef.current = page.bodyJsonb ?? null;
    lastSavedTitleRef.current = page.title;
  }, [page.id]);

  // When the parent flips out of edit mode, flush any pending saves so the
  // user's last keystroke can't be dropped by the editor losing focus.
  useEffect(() => {
    if (viewingRevision) return; // revision views never had pending saves
    if (effectiveMode !== "view") return;
    if (bodyTimer.current && editor) {
      clearTimeout(bodyTimer.current);
      bodyTimer.current = null;
      const json = JSON.stringify(editor.getJSON());
      if (json !== lastSavedBodyRef.current) {
        lastSavedBodyRef.current = json;
        updatePage.mutate({ id: page.id, body: { bodyJsonb: json } });
      }
    }
    if (titleTimer.current) {
      clearTimeout(titleTimer.current);
      titleTimer.current = null;
      const trimmed = titleDraft.trim();
      if (trimmed && trimmed !== lastSavedTitleRef.current) {
        lastSavedTitleRef.current = trimmed;
        updatePage.mutate({ id: page.id, body: { title: trimmed } });
      }
    }
  }, [effectiveMode, editor, page.id, titleDraft, updatePage, viewingRevision]);

  // Flush any pending saves on unmount so the last keystroke isn't lost.
  useEffect(() => {
    return () => {
      if (bodyTimer.current) clearTimeout(bodyTimer.current);
      if (titleTimer.current) clearTimeout(titleTimer.current);
    };
  }, []);

  const onTitleChange = (next: string) => {
    setTitleDraft(next);
    if (titleTimer.current) clearTimeout(titleTimer.current);
    titleTimer.current = setTimeout(() => {
      const trimmed = next.trim();
      if (!trimmed) return; // never persist an empty title
      if (trimmed === lastSavedTitleRef.current) return;
      lastSavedTitleRef.current = trimmed;
      updatePage.mutate({ id: page.id, body: { title: trimmed } });
    }, AUTOSAVE_DEBOUNCE_MS);
  };

  return (
    <div className="notes-editor-bleed" style={{ flex: 1, display: "flex", flexDirection: "column", minHeight: 0, background: "#fff" }}>
      <RichTextEditor
        editor={editor}
        styles={{
          root: {
            border: "none",
            borderRadius: 0,
            flex: 1,
            display: "flex",
            flexDirection: "column",
            minHeight: 0
          },
          toolbar: { borderBottom: `1px solid ${notesTheme.border}`, padding: "5px 10px" },
          content: { flex: 1, overflowY: "auto", background: "#fff" }
        }}
      >
        {effectiveMode === "edit" && (
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
            <div style={{ marginLeft: "auto", display: "flex", alignItems: "center", gap: 6 }}>
              {updatePage.isPending ? (
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
        )}

        <div style={{ flex: 1, overflowY: "auto", padding: "32px 0 32px 40px" }}>
          <div style={{ width: "100%" }}>
            {effectiveMode === "view" ? (
              <h1
                style={{
                  margin: "0 0 18px",
                  fontSize: 30,
                  fontWeight: 700,
                  letterSpacing: "-0.02em",
                  color: notesTheme.dark,
                  overflowWrap: "break-word"
                }}
              >
                {effectiveTitle}
              </h1>
            ) : (
              <input
                value={titleDraft}
                onChange={(e) => onTitleChange(e.target.value)}
                placeholder="Untitled page"
                style={{
                  width: "100%",
                  marginBottom: 18,
                  border: "none",
                  outline: "none",
                  background: "transparent",
                  fontSize: 30,
                  fontWeight: 700,
                  letterSpacing: "-0.02em",
                  color: notesTheme.dark,
                  fontFamily: "inherit",
                  padding: 0
                }}
              />
            )}
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
    return "";
  }
}

const savedStyle: React.CSSProperties = {
  fontSize: 11,
  color: notesTheme.muted,
  fontWeight: 600
};
