import { useEffect, useRef, useState } from "react";
import type { PartialBlock } from "@blocknote/core";
import { useCreateBlockNote } from "@blocknote/react";
import { BlockNoteView } from "@blocknote/mantine";
import { ActionIcon, Tooltip } from "@mantine/core";
import { PageDto } from "@/api/content";
import { useUpdatePage } from "@/hooks/useContent";
import { useYjsDocument } from "@/lib/yjs/useYjsDocument";
import { YjsEditor } from "@/lib/yjs/YjsEditor";
import { ConnectionStatusPill } from "@/lib/yjs/ConnectionStatusPill";
import { notesTheme } from "./notesTheme";

type Props = {
  page: PageDto;
  mode: "view" | "edit";
  revisionOverride?: {
    versionNumber: number;
    title: string;
    bodyJsonb: string;
  } | null;
};

const TITLE_AUTOSAVE_DEBOUNCE_MS = 600;

export function PageOverview({ page, mode, revisionOverride }: Props) {
  const viewingRevision = revisionOverride != null;

  if (viewingRevision && revisionOverride) {
    return (
      <PageShell
        title={revisionOverride.title}
        editableTitle={false}
        onTitleChange={() => {}}
        rightSlot={null}
      >
        <RevisionEditor
          key={revisionOverride.versionNumber}
          rawContent={revisionOverride.bodyJsonb}
        />
      </PageShell>
    );
  }

  return (
    <LivePageEditor
      // Re-mount on page swap so the Yjs handle teardown + recreate runs
      // cleanly.
      key={page.id}
      page={page}
      mode={mode}
    />
  );
}

function LivePageEditor({ page, mode }: { page: PageDto; mode: "view" | "edit" }) {
  const [titleDraft, setTitleDraft] = useState(page.title);
  const [showSidebar, setShowSidebar] = useState(false);
  const updatePage = useUpdatePage();
  const titleTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lastSavedTitleRef = useRef<string | null>(page.title);
  const { handle, status, role } = useYjsDocument(`page:${page.id}`);

  // Reset title bookkeeping when the user navigates to a different page.
  // Page id is stable inside LivePageEditor (the parent re-keys on swap),
  // so this is a one-time setup; left as a useEffect for future safety.
  useEffect(() => {
    setTitleDraft(page.title);
    lastSavedTitleRef.current = page.title;
  }, [page.id, page.title]);

  // On leaving edit mode, flush the pending title save so a fast tab-out
  // doesn't drop the last keystroke.
  useEffect(() => {
    if (mode !== "view") return;
    if (!titleTimer.current) return;
    clearTimeout(titleTimer.current);
    titleTimer.current = null;
    const trimmed = titleDraft.trim();
    if (trimmed && trimmed !== lastSavedTitleRef.current) {
      lastSavedTitleRef.current = trimmed;
      updatePage.mutate({ id: page.id, body: { title: trimmed } });
    }
  }, [mode, page.id, titleDraft, updatePage]);

  useEffect(() => {
    return () => {
      if (titleTimer.current) clearTimeout(titleTimer.current);
    };
  }, []);

  const onTitleChange = (next: string) => {
    setTitleDraft(next);
    if (titleTimer.current) clearTimeout(titleTimer.current);
    titleTimer.current = setTimeout(() => {
      const trimmed = next.trim();
      if (!trimmed) return;
      if (trimmed === lastSavedTitleRef.current) return;
      lastSavedTitleRef.current = trimmed;
      updatePage.mutate({ id: page.id, body: { title: trimmed } });
    }, TITLE_AUTOSAVE_DEBOUNCE_MS);
  };

  const sidebarToggle = (
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
  );

  return (
    <PageShell
      title={titleDraft}
      editableTitle={mode === "edit"}
      onTitleChange={onTitleChange}
      rightSlot={
        mode === "edit" ? (
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <ConnectionStatusPill status={status} role={role} />
            {sidebarToggle}
          </div>
        ) : null
      }
      // View mode has no header strip; pin the toggle to the upper-right of
      // the editor area so readers can still surface the threads list.
      floatingSlot={mode === "view" ? sidebarToggle : null}
    >
      {handle ? (
        <YjsEditor
          handle={handle}
          // Edit mode AND role allows writes. A viewer in "edit mode"
          // still renders read-only — the role gate is the authoritative
          // check.
          editable={mode === "edit" && role === "editor"}
          role={role}
          // Threads sidebar can be open in either mode now — in view mode the
          // editor is non-editable, but the sidebar still shows the
          // thread list for read-along.
          showSidebar={showSidebar}
        />
      ) : null}
    </PageShell>
  );
}

function RevisionEditor({ rawContent }: { rawContent: string }) {
  const initialContent = parseInitialContent(rawContent);
  const editor = useCreateBlockNote({
    initialContent,
    placeholders: { default: "Type to start writing…" }
  });
  editor.isEditable = false;
  return <BlockNoteView editor={editor} editable={false} theme="light" />;
}

function PageShell({
  title,
  editableTitle,
  onTitleChange,
  rightSlot,
  floatingSlot,
  children
}: {
  title: string;
  editableTitle: boolean;
  onTitleChange: (next: string) => void;
  rightSlot: React.ReactNode;
  // Rendered absolutely positioned in the upper-right of the editor area.
  // Used in view mode where the header strip isn't shown but we still want
  // the threads-sidebar toggle visible.
  floatingSlot?: React.ReactNode;
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
        background: "#fff",
        position: "relative"
      }}
    >
      {rightSlot && (
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
      )}

      {floatingSlot && (
        <div
          style={{
            position: "absolute",
            top: 16,
            right: 16,
            zIndex: 10
          }}
        >
          {floatingSlot}
        </div>
      )}

      <div style={{ flex: 1, overflowY: "auto", padding: "32px 0 32px 40px" }}>
        <div style={{ width: "100%" }}>
          {editableTitle ? (
            <input
              value={title}
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
          ) : (
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
              {title}
            </h1>
          )}
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
