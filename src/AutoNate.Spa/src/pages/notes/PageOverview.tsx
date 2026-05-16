import { useEffect, useRef, useState } from "react";
import type { PartialBlock } from "@blocknote/core";
import { useCreateBlockNote } from "@blocknote/react";
import { BlockNoteView } from "@blocknote/mantine";
import { PageDto } from "@/api/content";
import { useUpdatePage } from "@/hooks/useContent";
import { notesTheme } from "./notesTheme";

type Props = {
  page: PageDto;
  mode: "view" | "edit";
  // When set, the editor renders this historical revision's content instead
  // of the current page body. Edit mode is implicitly disabled (the banner +
  // Restore button handle promotion back to current). `versionNumber` is
  // part of the editor's identity so React tears down + re-creates BlockNote
  // when the user navigates between revisions.
  revisionOverride?: {
    versionNumber: number;
    title: string;
    bodyJsonb: string;
  } | null;
};

const AUTOSAVE_DEBOUNCE_MS = 600;

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

  const editor = useCreateBlockNote({
    initialContent: parseInitialContent(effectiveBody),
    placeholders: { default: "Type to start writing…" }
  });

  // Sync editable flag whenever mode flips. Revision view is always read-only.
  useEffect(() => {
    editor.isEditable = effectiveMode === "edit";
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

  // Body autosave subscription. Detaches on re-mount / dep change so we never
  // stack listeners that would double-save.
  useEffect(() => {
    if (viewingRevision) return;
    const unsubscribe = editor.onChange((ed) => {
      // Never autosave a revision view or a read-only render; the editable
      // check below is belt-and-suspenders against an edge case where the
      // user toggles mode mid-keystroke.
      if (effectiveMode !== "edit") return;
      if (bodyTimer.current) clearTimeout(bodyTimer.current);
      bodyTimer.current = setTimeout(() => {
        const json = JSON.stringify(ed.document);
        if (json === lastSavedBodyRef.current) return;
        lastSavedBodyRef.current = json;
        updatePage.mutate({ id: page.id, body: { bodyJsonb: json } });
      }, AUTOSAVE_DEBOUNCE_MS);
    });
    return unsubscribe;
  }, [editor, effectiveMode, page.id, updatePage, viewingRevision]);

  // When the parent flips out of edit mode, flush any pending saves so the
  // user's last keystroke can't be dropped by the editor losing focus.
  useEffect(() => {
    if (viewingRevision) return;
    if (effectiveMode !== "view") return;
    if (bodyTimer.current) {
      clearTimeout(bodyTimer.current);
      bodyTimer.current = null;
      const json = JSON.stringify(editor.document);
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
      {effectiveMode === "edit" && (
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
          <BlockNoteView editor={editor} editable={effectiveMode === "edit"} theme="light" />
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
