import { Suspense, lazy, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Tooltip } from "@mantine/core";
import { CabinetDto, NoteDto, PageDto } from "@/api/content";
import {
  useCopyNote,
  useCopyPage,
  useDeleteNote,
  useDeletePage,
  useMoveNote,
  useNoteVersion,
  usePageVersion,
  useRestoreNoteVersion,
  useRestorePageVersion,
  useToggleFavoritePage,
  useUpdatePage
} from "@/hooks/useContent";
import { NOTE_KIND_META, cabinetColorFor, defaultCabinetIcon, notesTheme } from "./notesTheme";
import { EditorTab, NotebookWithPages, PageTreeNode } from "./types";
import { PageOverview } from "./PageOverview";
import { VisualTextEditor } from "./VisualTextEditor";
import { NapkinEditor } from "./NapkinEditor";
// Lazy-loaded — DiagramEditor pulls in the drawio postMessage/Yjs glue,
// which isn't needed unless the user actually opens a diagram tab.
// React.lazy only accepts default exports, so shim the named export.
const DiagramEditor = lazy(() =>
  import("./DiagramEditor").then((m) => ({ default: m.DiagramEditor }))
);
import {
  DndContext,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent
} from "@dnd-kit/core";
import {
  SortableContext,
  horizontalListSortingStrategy,
  arrayMove,
  useSortable
} from "@dnd-kit/sortable";
import { CSS as DndCss } from "@dnd-kit/utilities";
import { HistoryModal } from "./HistoryModal";
import { ShareModal } from "./ShareModal";
import { MoveCopyModal } from "./MoveCopyModal";
import { ConfirmDialog } from "./ConfirmDialog";
import { exportToPdf } from "./exportToPdf";

// Identity of a revision the user is browsing. Scoped to a specific page or
// note id so switching tabs/pages clears it. The content fetch lives in a
// react-query hook keyed off (kind, id, versionNumber), so a row click in
// the modal just sets this state and lets Suspense/loading happen below.
type RevisionRef =
  | { kind: "page"; pageId: string; versionNumber: number }
  | { kind: "note"; noteId: string; versionNumber: number };

type Props = {
  page: PageDto | null;
  pageNode: PageTreeNode | null;
  cabinet: CabinetDto | null;
  notebook: NotebookWithPages | null;
  tabs: EditorTab[];
  activeTabId: string;
  notes: NoteDto[];
  onSwitchTab: (tabId: string) => void;
  onCloseTab: (tabId: string) => void;
  onNewNote: () => void;
  // Called after a drag-and-drop reorder of note tabs. `orderedNoteIds`
  // is the new sequence the user dropped them into; the caller persists
  // sortOrder updates from this.
  onReorderNotes?: (orderedNoteIds: string[]) => void;
  // Called after a successful page delete — parent clears active page id.
  onPageDeleted?: () => void;
  // Called after a successful note delete from the ellipsis menu — parent
  // closes the tab and removes the note from local state.
  onNoteDeleted?: (noteId: string) => void;
  // Project id is needed for the move/copy destination picker.
  projectId: string | null;
};

export function EditorPane({
  page,
  pageNode,
  cabinet,
  notebook,
  tabs,
  activeTabId,
  notes,
  onSwitchTab,
  onCloseTab,
  onNewNote,
  onReorderNotes,
  onPageDeleted,
  onNoteDeleted,
  projectId
}: Props) {
  const navigate = useNavigate();
  // All hooks must run on every render — the empty-state early return below
  // must not skip them. Bug we hit before: useState/useEffect lived after the
  // null-page early return, so hook call counts differed between "no page"
  // and "page selected" renders and React logged "Expected static flag was
  // missing".
  const activeTab = tabs.find((t) => t.id === activeTabId) ?? tabs[0];
  const onPageTab = activeTab?.kind === "page";
  const activeNoteId =
    activeTab && activeTab.kind !== "page"
      ? (activeTab as Extract<EditorTab, { noteId: string }>).noteId
      : null;
  const [pageEditMode, setPageEditMode] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [shareOpen, setShareOpen] = useState(false);
  const [moreOpen, setMoreOpen] = useState(false);
  const [moveCopyOpen, setMoveCopyOpen] = useState<"move" | "copy" | null>(null);
  const [moveCopyError, setMoveCopyError] = useState<string | null>(null);
  const [deleteConfirmOpen, setDeleteConfirmOpen] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [revision, setRevision] = useState<RevisionRef | null>(null);
  const toggleFavorite = useToggleFavoritePage();
  const restorePageVersion = useRestorePageVersion();
  const restoreNoteVersion = useRestoreNoteVersion(page?.id ?? null);
  const updatePageMutation = useUpdatePage();
  const deletePageMutation = useDeletePage();
  const deleteNoteMutation = useDeleteNote(page?.id ?? null);
  const copyPageMutation = useCopyPage();
  const copyNoteMutation = useCopyNote();
  const moveNoteMutation = useMoveNote();

  // Reset to view mode when the active page changes or when the user navigates
  // away from the page tab — otherwise "edit mode" would silently persist
  // into the next page or note context.
  useEffect(() => {
    setPageEditMode(false);
  }, [page?.id, onPageTab]);

  // Clear revision state on any context shift (different page, different
  // note tab) so a banner from one document can't carry over to another.
  useEffect(() => {
    setRevision(null);
    setHistoryOpen(false);
  }, [page?.id, activeNoteId, onPageTab]);

  // Fetch the revision's full payload once the user picks a row. Each hook
  // is gated by the current `revision.kind` so only one query is live at a
  // time. staleTime: Infinity inside the hooks keeps the result cached.
  const pageRevisionQuery = usePageVersion(
    revision?.kind === "page" ? revision.pageId : null,
    revision?.kind === "page" ? revision.versionNumber : null
  );
  const noteRevisionQuery = useNoteVersion(
    revision?.kind === "note" ? revision.noteId : null,
    revision?.kind === "note" ? revision.versionNumber : null
  );

  const pageRevisionOverride = useMemo(() => {
    if (revision?.kind !== "page" || !pageRevisionQuery.data) return null;
    return {
      versionNumber: pageRevisionQuery.data.versionNumber,
      title: pageRevisionQuery.data.title,
      bodyJsonb: pageRevisionQuery.data.bodyJsonb
    };
  }, [revision, pageRevisionQuery.data]);

  const noteRevisionOverride = useMemo(() => {
    if (revision?.kind !== "note" || !noteRevisionQuery.data) return null;
    return {
      versionNumber: noteRevisionQuery.data.versionNumber,
      title: noteRevisionQuery.data.title,
      contentJsonb: noteRevisionQuery.data.contentJsonb
    };
  }, [revision, noteRevisionQuery.data]);

  const revisionCreatedAtUtc =
    revision?.kind === "page"
      ? pageRevisionQuery.data?.createdAtUtc ?? null
      : revision?.kind === "note"
        ? noteRevisionQuery.data?.createdAtUtc ?? null
        : null;
  const revisionAuthorName =
    revision?.kind === "page"
      ? pageRevisionQuery.data?.createdByName ?? null
      : revision?.kind === "note"
        ? noteRevisionQuery.data?.createdByName ?? null
        : null;
  const revisionLoading =
    (revision?.kind === "page" && pageRevisionQuery.isLoading) ||
    (revision?.kind === "note" && noteRevisionQuery.isLoading);
  const restoreBusy =
    restorePageVersion.isPending || restoreNoteVersion.isPending;

  // Only block on `page` — a viewer who's been per-page-shared a page won't
  // have access to the cabinet/notebook list endpoints (those return empty
  // for them), but the page itself loads fine via its dedicated allow-grant.
  // The breadcrumb degrades to just the page title in that case.
  if (!page) {
    return (
      <main
        style={{
          flex: 1,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: "#fff",
          color: notesTheme.muted,
          fontSize: 13
        }}
      >
        <div style={{ textAlign: "center", maxWidth: 360, padding: 24 }}>
          <i
            className="fa fa-book-open"
            style={{ fontSize: 32, color: notesTheme.border, display: "block", marginBottom: 12 }}
          />
          <div style={{ fontWeight: 700, color: notesTheme.dark, marginBottom: 4 }}>
            Pick a page to get started
          </div>
          <div>
            Select a cabinet on the left, expand a notebook, and choose a page — the editor
            will load here.
          </div>
        </div>
      </main>
    );
  }

  const cabinetColor = cabinet ? cabinetColorFor(cabinet.id) : notesTheme.muted;
  const cabinetIcon = cabinet?.icon ?? defaultCabinetIcon();
  const activeNote =
    activeTab && activeTab.kind !== "page"
      ? notes.find((n) => n.id === (activeTab as Extract<EditorTab, { noteId: string }>).noteId) ?? null
      : null;

  // History is contextual: on the page tab → page versions; on any note
  // tab → that note's versions. Disabled when there's no valid target
  // (note tab with no resolved note row yet) — see button disabled prop.
  const onHistoryClick = () => setHistoryOpen(true);

  const onRestoreClick = () => {
    if (!revision) return;
    if (revision.kind === "page") {
      restorePageVersion.mutate(
        { pageId: revision.pageId, versionNumber: revision.versionNumber },
        {
          onSuccess: () => {
            setRevision(null);
          }
        }
      );
    } else {
      restoreNoteVersion.mutate(
        { noteId: revision.noteId, versionNumber: revision.versionNumber },
        {
          onSuccess: () => {
            setRevision(null);
          }
        }
      );
    }
  };

  // Which kind of editor is rendered for the active tab determines whose
  // revision (if any) is overlaid. Computed once for readability below.
  const showPageOverride =
    revision?.kind === "page" && onPageTab && pageRevisionOverride != null;
  const showNoteOverride =
    revision?.kind === "note" &&
    activeNote != null &&
    revision.noteId === activeNote.id &&
    noteRevisionOverride != null;

  return (
    <main
      style={{
        flex: 1,
        display: "flex",
        flexDirection: "column",
        minWidth: 0,
        background: "#fff"
      }}
    >
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 12,
          padding: "6px 10px 6px 16px",
          borderBottom: `1px solid ${notesTheme.border}`,
          background: "#fff",
          flexShrink: 0
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 6,
            color: notesTheme.muted,
            fontSize: 11,
            flex: 1,
            minWidth: 0,
            overflow: "hidden"
          }}
        >
          <i className={`fa ${cabinetIcon}`} style={{ color: cabinetColor, fontSize: 10 }} />
          {cabinet && (
            <>
              <span>{cabinet.name}</span>
              <i className="fa fa-chevron-right" style={{ fontSize: 8 }} />
            </>
          )}
          {notebook && (
            <>
              <span>{notebook.name}</span>
              <i className="fa fa-chevron-right" style={{ fontSize: 8 }} />
            </>
          )}
          <strong style={{ color: notesTheme.dark, fontWeight: 700 }}>{page.title}</strong>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 2 }}>
          {onPageTab && !revision && (
            <HBtn
              icon="fa-pen"
              title={pageEditMode ? "Stop editing" : "Edit page"}
              active={pageEditMode}
              onClick={() => setPageEditMode((m) => !m)}
            />
          )}
          <HBtn
            icon={page.isFavorited ? "fa-solid fa-star" : "fa-regular fa-star"}
            iconColor={page.isFavorited ? "#f59e0b" : undefined}
            title={page.isFavorited ? "Remove from favorites" : "Add to favorites"}
            disabled={toggleFavorite.isPending}
            onClick={() =>
              toggleFavorite.mutate({ id: page.id, favorited: !page.isFavorited })
            }
          />
          <HBtn
            icon="fa-share-nodes"
            title="Share"
            onClick={() => setShareOpen(true)}
          />
          <HBtn
            icon="fa-clock-rotate-left"
            title={onPageTab ? "Page history" : "Note history"}
            active={historyOpen || revision != null}
            disabled={!onPageTab && !activeNote}
            onClick={onHistoryClick}
          />
          <MoreMenu
            open={moreOpen}
            onOpenChange={setMoreOpen}
            showDelete={page.actorIsProjectOwner}
            // Note tabs delete just the note. Page tab deletes the entire
            // page (and cascade-deletes its notes server-side).
            onExportPdf={() => {
              setMoreOpen(false);
              exportToPdf({
                onPageTab,
                pageTitle: page.title,
                noteTitle: activeNote?.title ?? activeTab?.name ?? "Note"
              });
            }}
            onMove={() => {
              setMoreOpen(false);
              setMoveCopyError(null);
              setMoveCopyOpen("move");
            }}
            onCopy={() => {
              setMoreOpen(false);
              setMoveCopyError(null);
              setMoveCopyOpen("copy");
            }}
            onDelete={() => {
              setMoreOpen(false);
              setDeleteError(null);
              setDeleteConfirmOpen(true);
            }}
          />
        </div>
      </div>

      <TabStrip
        tabs={tabs}
        activeTabId={activeTab?.id}
        onSwitchTab={onSwitchTab}
        onCloseTab={onCloseTab}
        onNewNote={onNewNote}
        onReorderNotes={onReorderNotes}
      />

      {revision != null && (
        <RevisionBanner
          versionNumber={revision.versionNumber}
          createdAtUtc={revisionCreatedAtUtc}
          authorName={revisionAuthorName}
          loading={revisionLoading}
          restoreBusy={restoreBusy}
          onRestore={onRestoreClick}
          onExit={() => setRevision(null)}
        />
      )}

      {activeTab?.kind === "page" && (
        <PageOverview
          page={page}
          mode={pageEditMode ? "edit" : "view"}
          revisionOverride={showPageOverride ? pageRevisionOverride : null}
        />
      )}
      {activeTab?.kind === "richtext" && (
        <VisualTextEditor
          note={activeNote}
          noteName={activeTab.name}
          revisionOverride={showNoteOverride ? noteRevisionOverride : null}
        />
      )}
      {activeTab?.kind === "drawing" && (
        <NapkinEditor
          note={activeNote}
          noteName={activeTab.name}
          revisionOverride={showNoteOverride ? noteRevisionOverride : null}
        />
      )}
      {activeTab?.kind === "diagram" && (
        <Suspense fallback={<div style={{ flex: 1 }} />}>
          <DiagramEditor
            note={activeNote}
            noteName={activeTab.name}
            revisionOverride={showNoteOverride ? noteRevisionOverride : null}
          />
        </Suspense>
      )}

      {historyOpen && onPageTab && (
        <HistoryModal
          kind="page"
          pageId={page.id}
          currentTitle={page.title}
          currentUpdatedAtUtc={page.updatedAtUtc}
          onSelect={(versionNumber) => {
            setRevision({ kind: "page", pageId: page.id, versionNumber });
            setHistoryOpen(false);
          }}
          onClose={() => setHistoryOpen(false)}
        />
      )}
      {historyOpen && !onPageTab && activeNote && (
        <HistoryModal
          kind="note"
          noteId={activeNote.id}
          currentTitle={activeNote.title ?? activeTab?.name ?? "Note"}
          currentUpdatedAtUtc={activeNote.updatedAtUtc}
          onSelect={(versionNumber) => {
            setRevision({
              kind: "note",
              noteId: activeNote.id,
              versionNumber
            });
            setHistoryOpen(false);
          }}
          onClose={() => setHistoryOpen(false)}
        />
      )}

      {shareOpen && (
        <ShareModal
          pageId={page.id}
          pageTitle={page.title}
          onClose={() => setShareOpen(false)}
        />
      )}

      {moveCopyOpen && (
        <MoveCopyModal
          mode={moveCopyOpen}
          itemKind={onPageTab ? "page" : "note"}
          itemId={onPageTab ? page.id : (activeNote?.id ?? "")}
          itemTitle={onPageTab ? page.title : (activeNote?.title ?? activeTab?.name ?? "Note")}
          projectId={projectId}
          sourceNotebookId={onPageTab ? page.notebookId : null}
          sourceParentPageId={onPageTab ? page.parentPageId : null}
          sourcePageId={!onPageTab ? page.id : null}
          busy={
            (onPageTab && (updatePageMutation.isPending || copyPageMutation.isPending)) ||
            (!onPageTab && (moveNoteMutation.isPending || copyNoteMutation.isPending))
          }
          error={moveCopyError}
          onClose={() => {
            if (
              updatePageMutation.isPending ||
              copyPageMutation.isPending ||
              moveNoteMutation.isPending ||
              copyNoteMutation.isPending
            ) {
              return;
            }
            setMoveCopyOpen(null);
            setMoveCopyError(null);
          }}
          onConfirm={async (dest) => {
            try {
              if (onPageTab) {
                // Notebook destination → top-level page (parentPageId=null).
                // Page destination → sub-page under that parent in the dest
                // notebook (the picker carries the destination notebookId on
                // PageDestination so we don't have to look it up here).
                const destNotebookId = dest.kind === "notebook" ? dest.id : dest.notebookId;
                const parentPageId = dest.kind === "page" ? dest.id : null;
                if (moveCopyOpen === "move") {
                  const moved = await updatePageMutation.mutateAsync({
                    id: page.id,
                    body: {
                      notebookId: destNotebookId,
                      parentPageId,
                      parentPageIdSet: true
                    }
                  });
                  setMoveCopyOpen(null);
                  // Locator is stable across moves — re-route so URL reflects
                  // the new ancestor chain.
                  navigate(`/notes/${moved.locator}`, { replace: true });
                } else {
                  const copy = await copyPageMutation.mutateAsync({
                    id: page.id,
                    notebookId: destNotebookId,
                    parentPageId
                  });
                  setMoveCopyOpen(null);
                  navigate(`/notes/${copy.locator}`);
                }
              } else {
                if (dest.kind !== "page") {
                  setMoveCopyError("Pick a page as the destination.");
                  return;
                }
                if (!activeNote) {
                  setMoveCopyError("Select a note first.");
                  return;
                }
                if (moveCopyOpen === "move") {
                  await moveNoteMutation.mutateAsync({
                    id: activeNote.id,
                    sourcePageId: page.id,
                    destPageId: dest.id
                  });
                  setMoveCopyOpen(null);
                  // Jump to the destination page so the user sees the note
                  // in its new location. We land on the destination page
                  // (no /n segment) and let the SPA's URL-writeback effect
                  // resync once the destination's notes query refetches and
                  // the moved note's new pageNoteIndex is known.
                  navigate(`/notes/${dest.locator}`);
                } else {
                  await copyNoteMutation.mutateAsync({
                    id: activeNote.id,
                    sourcePageId: page.id,
                    destPageId: dest.id
                  });
                  setMoveCopyOpen(null);
                  navigate(`/notes/${dest.locator}`);
                }
              }
              setMoveCopyError(null);
            } catch (err) {
              setMoveCopyError(describeError(err));
            }
          }}
        />
      )}

      {deleteConfirmOpen && (
        <ConfirmDialog
          icon="fa-trash"
          title={
            onPageTab
              ? `Delete “${page.title}”?`
              : `Delete “${activeNote?.title ?? activeTab?.name ?? "Note"}”?`
          }
          destructive
          body={
            onPageTab ? (
              <>
                This permanently deletes the page and{" "}
                <strong>every note, child page, and attachment inside it</strong>.
                This cannot be undone.
              </>
            ) : (
              <>This permanently deletes the note and all of its content. This cannot be undone.</>
            )
          }
          confirmLabel={onPageTab ? "Delete page" : "Delete note"}
          busy={onPageTab ? deletePageMutation.isPending : deleteNoteMutation.isPending}
          error={deleteError}
          onCancel={() => {
            if (deletePageMutation.isPending || deleteNoteMutation.isPending) return;
            setDeleteConfirmOpen(false);
            setDeleteError(null);
          }}
          onConfirm={async () => {
            try {
              if (onPageTab) {
                await deletePageMutation.mutateAsync(page.id);
                setDeleteConfirmOpen(false);
                setDeleteError(null);
                onPageDeleted?.();
              } else if (activeNote) {
                await deleteNoteMutation.mutateAsync(activeNote.id);
                setDeleteConfirmOpen(false);
                setDeleteError(null);
                onNoteDeleted?.(activeNote.id);
              }
            } catch (err) {
              setDeleteError(describeError(err));
            }
          }}
        />
      )}
    </main>
  );
}

// Map of describeError mirrors NotesPage helper — kept local so this file
// doesn't reach into the parent module.
function describeError(err: unknown): string {
  if (typeof err === "object" && err && "response" in err) {
    const resp = (err as { response?: { data?: { error?: string; message?: string } } }).response;
    return resp?.data?.error ?? resp?.data?.message ?? "Request failed.";
  }
  return err instanceof Error ? err.message : "Request failed.";
}


function RevisionBanner({
  versionNumber,
  createdAtUtc,
  authorName,
  loading,
  restoreBusy,
  onRestore,
  onExit
}: {
  versionNumber: number;
  createdAtUtc: string | null;
  authorName: string | null;
  loading: boolean;
  restoreBusy: boolean;
  onRestore: () => void;
  onExit: () => void;
}) {
  const dateLabel = createdAtUtc
    ? new Date(createdAtUtc).toLocaleString(undefined, {
        year: "numeric",
        month: "short",
        day: "numeric",
        hour: "numeric",
        minute: "2-digit"
      })
    : null;
  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: 12,
        padding: "8px 18px",
        background: "#fff8e1",
        borderBottom: `1px solid ${notesTheme.warning}`,
        color: "#7a5a00",
        fontSize: 12.5,
        flexShrink: 0
      }}
    >
      <i className="fa fa-clock-rotate-left" style={{ color: notesTheme.warning }} />
      <div style={{ flex: 1, minWidth: 0 }}>
        <strong style={{ fontWeight: 700, color: "#5e4500" }}>
          Viewing revision v{versionNumber}
        </strong>
        {dateLabel && (
          <span style={{ marginLeft: 8, color: "#7a5a00" }}>
            from {dateLabel}
          </span>
        )}
        {authorName && (
          <span style={{ marginLeft: 8, color: "#7a5a00" }}>
            by <strong style={{ fontWeight: 700, color: "#5e4500" }}>{authorName}</strong>
          </span>
        )}
        {loading && !dateLabel && (
          <span style={{ marginLeft: 8 }}>
            <i className="fa fa-spinner fa-spin" style={{ marginRight: 4 }} />
            loading…
          </span>
        )}
        <span style={{ marginLeft: 12, color: "#9c7600" }}>
          Read-only — click Restore to make this the current version.
        </span>
      </div>
      <button
        type="button"
        onClick={onExit}
        title="Return to current"
        style={{
          background: "transparent",
          border: "none",
          color: "#7a5a00",
          cursor: "pointer",
          fontSize: 12,
          fontWeight: 600,
          padding: "4px 8px",
          borderRadius: 3
        }}
      >
        <i className="fa fa-xmark" style={{ marginRight: 4 }} /> Exit
      </button>
      <button
        type="button"
        onClick={onRestore}
        disabled={loading || restoreBusy}
        style={{
          background: notesTheme.warning,
          border: "none",
          borderRadius: 4,
          color: "#fff",
          fontWeight: 700,
          fontSize: 12,
          padding: "6px 14px",
          cursor: loading || restoreBusy ? "default" : "pointer",
          opacity: loading || restoreBusy ? 0.7 : 1,
          fontFamily: "inherit"
        }}
      >
        {restoreBusy ? "Restoring…" : "Restore"}
      </button>
    </div>
  );
}

// Popover menu attached to the header's ellipsis button. Renders four
// actions: Export PDF, Move, Copy, Delete. Delete is conditionally rendered
// (Owner-only — the backend enforces too, but the SPA hides the option for
// non-owners to match the design). Click-outside / Escape closes.
function MoreMenu({
  open,
  onOpenChange,
  showDelete,
  onExportPdf,
  onMove,
  onCopy,
  onDelete
}: {
  open: boolean;
  onOpenChange: (next: boolean) => void;
  showDelete: boolean;
  onExportPdf: () => void;
  onMove: () => void;
  onCopy: () => void;
  onDelete: () => void;
}) {
  const wrapRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!wrapRef.current?.contains(e.target as Node)) onOpenChange(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onOpenChange(false);
    };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open, onOpenChange]);

  return (
    <div ref={wrapRef} style={{ position: "relative", display: "inline-flex" }}>
      <HBtn
        icon="fa-ellipsis"
        title="More"
        active={open}
        onClick={() => onOpenChange(!open)}
      />
      {open && (
        <div
          onClick={(e) => e.stopPropagation()}
          style={{
            position: "absolute",
            top: "calc(100% + 4px)",
            right: 0,
            minWidth: 200,
            background: "#fff",
            border: `1px solid ${notesTheme.border}`,
            borderRadius: 4,
            boxShadow: "0 6px 18px rgba(0,0,0,0.12)",
            padding: 4,
            zIndex: 60
          }}
        >
          <MoreMenuItem icon="fa-file-pdf" label="Export PDF" onClick={onExportPdf} />
          <MoreMenuItem icon="fa-arrow-right" label="Move" onClick={onMove} />
          <MoreMenuItem icon="fa-copy" label="Copy" onClick={onCopy} />
          {showDelete && (
            <>
              <div style={{ height: 1, background: notesTheme.border, margin: "4px 2px" }} />
              <MoreMenuItem icon="fa-trash" label="Delete" danger onClick={onDelete} />
            </>
          )}
        </div>
      )}
    </div>
  );
}

function MoreMenuItem({
  icon,
  label,
  onClick,
  danger
}: {
  icon: string;
  label: string;
  onClick: () => void;
  danger?: boolean;
}) {
  const [hover, setHover] = useState(false);
  const color = danger ? notesTheme.danger : notesTheme.dark;
  return (
    <button
      type="button"
      onClick={onClick}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        width: "100%",
        display: "flex",
        alignItems: "center",
        gap: 8,
        background: hover ? (danger ? "#fee" : notesTheme.hover) : "transparent",
        border: "none",
        borderRadius: 4,
        padding: "6px 10px",
        textAlign: "left",
        cursor: "pointer",
        color,
        fontSize: 12,
        fontWeight: 600,
        fontFamily: "inherit"
      }}
    >
      <i className={`fa ${icon}`} style={{ width: 14, fontSize: 11 }} />
      {label}
    </button>
  );
}

function HBtn({
  icon,
  title,
  active,
  disabled,
  iconColor,
  onClick
}: {
  // Accepts either a single FA name (e.g. "fa-pen", treated as solid via the
  // default "fa" prefix) or a full FA class string (e.g. "fa-regular fa-star")
  // when a non-default style is needed.
  icon: string;
  title: string;
  active?: boolean;
  disabled?: boolean;
  iconColor?: string;
  onClick?: () => void;
}) {
  const [hover, setHover] = useState(false);
  const background = active
    ? notesTheme.selected
    : hover && !disabled
      ? notesTheme.rowHover
      : "transparent";
  const isStyledClass = icon.includes(" ");
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      disabled={disabled}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        width: 28,
        height: 28,
        border: "none",
        borderRadius: 3,
        background,
        color: active ? notesTheme.primary : notesTheme.dark,
        cursor: disabled ? "default" : "pointer",
        opacity: disabled ? 0.6 : 1,
        fontSize: 12
      }}
    >
      <i className={isStyledClass ? icon : `fa ${icon}`} style={iconColor ? { color: iconColor } : undefined} />
    </button>
  );
}

function TabStrip({
  tabs,
  activeTabId,
  onSwitchTab,
  onCloseTab,
  onNewNote,
  onReorderNotes
}: {
  tabs: EditorTab[];
  activeTabId: string | undefined;
  onSwitchTab: (tabId: string) => void;
  onCloseTab: (tabId: string) => void;
  onNewNote: () => void;
  onReorderNotes?: (orderedNoteIds: string[]) => void;
}) {
  // The page tab is always present and always first. We pin it outside the
  // scroller so it stays visible even when the user has so many note tabs
  // open that the rest have to scroll horizontally.
  const pageTab = tabs.find((t) => t.kind === "page") ?? null;
  const noteTabs = tabs.filter((t) => t.kind !== "page");

  const scrollerRef = useRef<HTMLDivElement>(null);
  // `hasOverflow` gates whether arrows are rendered at all — once they
  // appear they stay in the layout, even when one direction is exhausted
  // (we just disable that side). The toggle would otherwise shift the
  // tab strip horizontally and could make the user click a tab's close
  // button by mistake.
  const [hasOverflow, setHasOverflow] = useState(false);
  const [canScrollLeft, setCanScrollLeft] = useState(false);
  const [canScrollRight, setCanScrollRight] = useState(false);

  // Snapshot the scroller's overflow state. Called after every scroll, every
  // resize (own or window), and any time the tab set changes.
  const updateCanScroll = useCallback(() => {
    const el = scrollerRef.current;
    if (!el) {
      setHasOverflow(false);
      setCanScrollLeft(false);
      setCanScrollRight(false);
      return;
    }
    // 2px tolerance handles sub-pixel rounding — without it the right
    // arrow stays "enabled" at ~99.9% scrolled and the left arrow
    // enables for a single sub-pixel of forward scroll on first click.
    setHasOverflow(el.scrollWidth > el.clientWidth + 1);
    setCanScrollLeft(el.scrollLeft > 1);
    setCanScrollRight(el.scrollLeft + el.clientWidth < el.scrollWidth - 1);
  }, []);

  useEffect(() => {
    updateCanScroll();
    const el = scrollerRef.current;
    if (!el) return;
    el.addEventListener("scroll", updateCanScroll, { passive: true });
    const ro = new ResizeObserver(updateCanScroll);
    ro.observe(el);
    return () => {
      el.removeEventListener("scroll", updateCanScroll);
      ro.disconnect();
    };
  }, [updateCanScroll, noteTabs.length]);

  // Keep the active note tab in view when the user switches to it via the
  // explorer or a deep-link — otherwise an offscreen tab activates but the
  // strip leaves them looking at a different segment.
  useEffect(() => {
    if (!activeTabId) return;
    const el = scrollerRef.current;
    if (!el) return;
    const target = el.querySelector(
      `[data-tab-id="${CSS.escape(activeTabId)}"]`
    ) as HTMLElement | null;
    if (!target) return; // page tab lives outside the scroller — nothing to do
    const tRect = target.getBoundingClientRect();
    const eRect = el.getBoundingClientRect();
    if (tRect.left < eRect.left) {
      el.scrollBy({ left: tRect.left - eRect.left - 8, behavior: "smooth" });
    } else if (tRect.right > eRect.right) {
      el.scrollBy({ left: tRect.right - eRect.right + 8, behavior: "smooth" });
    }
  }, [activeTabId]);

  const stepScroll = (direction: -1 | 1) => {
    const el = scrollerRef.current;
    if (!el) return;
    // Scroll roughly the visible portion so one arrow click reveals a fresh
    // batch of tabs without skipping past adjacent ones.
    const step = Math.max(160, el.clientWidth * 0.6);
    el.scrollBy({ left: direction * step, behavior: "smooth" });
  };

  // Drag-to-reorder. PointerSensor with activationConstraint.distance keeps
  // single-click-to-switch behavior intact — a drag only starts after the
  // pointer moves ≥6px so casual clicks on the tab body still fire
  // onSwitchTab.
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } })
  );
  const noteTabIds = noteTabs
    .filter((t): t is Extract<EditorTab, { noteId: string }> => "noteId" in t)
    .map((t) => t.noteId);
  const onDragEnd = (event: DragEndEvent) => {
    if (!onReorderNotes) return;
    const { active, over } = event;
    if (!over || active.id === over.id) return;
    const oldIndex = noteTabIds.indexOf(String(active.id));
    const newIndex = noteTabIds.indexOf(String(over.id));
    if (oldIndex < 0 || newIndex < 0) return;
    onReorderNotes(arrayMove(noteTabIds, oldIndex, newIndex));
  };

  return (
    <div
      style={{
        display: "flex",
        alignItems: "flex-end",
        gap: 2,
        padding: "0 12px",
        borderBottom: `1px solid ${notesTheme.border}`,
        background: notesTheme.hover,
        height: 38,
        flexShrink: 0,
        // Hard cap height so a long row can't push the editor down.
        minWidth: 0
      }}
    >
      {pageTab && (
        <Tab
          key={pageTab.id}
          tab={pageTab}
          active={pageTab.id === activeTabId}
          onSwitch={() => onSwitchTab(pageTab.id)}
          onClose={() => onCloseTab(pageTab.id)}
        />
      )}
      {hasOverflow && (
        <ScrollArrow
          direction="left"
          disabled={!canScrollLeft}
          onClick={() => stepScroll(-1)}
        />
      )}
      <div
        ref={scrollerRef}
        className="notes-tab-scroller"
        style={{
          display: "flex",
          alignItems: "flex-end",
          gap: 2,
          flex: 1,
          minWidth: 0,
          overflowX: "auto",
          overflowY: "hidden"
        }}
      >
        <DndContext sensors={sensors} onDragEnd={onDragEnd}>
          <SortableContext items={noteTabIds} strategy={horizontalListSortingStrategy}>
            {noteTabs.map((t) =>
              "noteId" in t ? (
                <SortableTab
                  key={t.id}
                  tab={t}
                  active={t.id === activeTabId}
                  onSwitch={() => onSwitchTab(t.id)}
                  onClose={() => onCloseTab(t.id)}
                />
              ) : null
            )}
          </SortableContext>
        </DndContext>
      </div>
      {hasOverflow && (
        <ScrollArrow
          direction="right"
          disabled={!canScrollRight}
          onClick={() => stepScroll(1)}
        />
      )}
      <Tooltip label="New Note" withArrow position="bottom">
        <button
          type="button"
          onClick={onNewNote}
          aria-label="New Note"
          style={{
            display: "inline-flex",
            alignItems: "center",
            justifyContent: "center",
            background: "transparent",
            border: "none",
            // Row is 38px tall; leave a 3px breathing strip top/bottom.
            width: 32,
            height: 32,
            marginBottom: 3,
            marginLeft: 4,
            marginRight: 4,
            borderRadius: 4,
            color: notesTheme.muted,
            cursor: "pointer",
            fontSize: 16,
            fontFamily: "inherit",
            flexShrink: 0
          }}
          onMouseEnter={(e) => {
            e.currentTarget.style.background = notesTheme.rowHover;
            e.currentTarget.style.color = notesTheme.dark;
          }}
          onMouseLeave={(e) => {
            e.currentTarget.style.background = "transparent";
            e.currentTarget.style.color = notesTheme.muted;
          }}
        >
          <i className="fa fa-plus" />
        </button>
      </Tooltip>
    </div>
  );
}

function ScrollArrow({
  direction,
  disabled,
  onClick
}: {
  direction: "left" | "right";
  disabled?: boolean;
  onClick: () => void;
}) {
  const [hover, setHover] = useState(false);
  const label = direction === "left" ? "Scroll tabs left" : "Scroll tabs right";
  const showHover = hover && !disabled;
  return (
    <Tooltip label={label} withArrow position="bottom" disabled={disabled}>
      <button
        type="button"
        onClick={onClick}
        disabled={disabled}
        aria-label={label}
        onMouseEnter={() => setHover(true)}
        onMouseLeave={() => setHover(false)}
        style={{
          display: "inline-flex",
          alignItems: "center",
          justifyContent: "center",
          width: 24,
          height: 28,
          marginBottom: 3,
          background: showHover ? notesTheme.rowHover : "transparent",
          color: disabled
            ? notesTheme.border
            : showHover
              ? notesTheme.dark
              : notesTheme.muted,
          border: "none",
          borderRadius: 4,
          cursor: disabled ? "default" : "pointer",
          opacity: disabled ? 0.5 : 1,
          fontSize: 11,
          flexShrink: 0
        }}
      >
        <i className={`fa fa-chevron-${direction}`} />
      </button>
    </Tooltip>
  );
}

// Wraps a note Tab with dnd-kit's useSortable so the user can drag it to
// a new position. PointerSensor's 6px activation distance means a tap on
// the body of the tab still triggers onSwitch — only a real drag motion
// engages the reorder.
function SortableTab({
  tab,
  active,
  onSwitch,
  onClose
}: {
  tab: Extract<EditorTab, { noteId: string }>;
  active: boolean;
  onSwitch: () => void;
  onClose: () => void;
}) {
  const sortable = useSortable({ id: tab.noteId });
  const { setNodeRef, attributes, listeners, transform, transition, isDragging } = sortable;
  return (
    <div
      ref={setNodeRef}
      data-tab-id={tab.id}
      style={{
        flexShrink: 0,
        display: "inline-flex",
        alignItems: "flex-end",
        transform: DndCss.Transform.toString(transform),
        transition,
        // Lift the dragged item above its siblings; keep z-index small
        // so the editor's own UI (toolbars, etc.) still wins.
        zIndex: isDragging ? 5 : undefined,
        opacity: isDragging ? 0.9 : 1
      }}
      {...attributes}
      {...listeners}
    >
      <Tab tab={tab} active={active} onSwitch={onSwitch} onClose={onClose} />
    </div>
  );
}

function Tab({
  tab,
  active,
  onSwitch,
  onClose
}: {
  tab: EditorTab;
  active: boolean;
  onSwitch: () => void;
  onClose: () => void;
}) {
  const [hover, setHover] = useState(false);
  const isPage = tab.kind === "page";
  const meta = isPage ? null : NOTE_KIND_META[tab.kind];
  const icon = isPage ? "fa-file-lines" : meta?.icon ?? "fa-file";
  const iconColor = isPage ? notesTheme.primary : meta?.color ?? notesTheme.muted;

  return (
    <div
      onClick={onSwitch}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 7,
        padding: "0 10px",
        height: 30,
        background: active ? "#fff" : hover ? notesTheme.rowHover : "transparent",
        boxShadow: active ? `inset 0 -2px 0 ${notesTheme.primary}` : "none",
        borderTopLeftRadius: 4,
        borderTopRightRadius: 4,
        cursor: "pointer",
        fontSize: 12,
        color: active ? notesTheme.dark : notesTheme.muted,
        fontWeight: active ? 700 : 600,
        position: "relative",
        top: 1,
        borderLeft: "1px solid transparent",
        borderRight: "1px solid transparent"
      }}
    >
      <i className={`fa ${icon}`} style={{ fontSize: 11, color: iconColor }} />
      <span>{tab.name}</span>
      {!isPage && (
        <button
          type="button"
          onClick={(e) => {
            e.stopPropagation();
            onClose();
          }}
          title="Close note"
          style={{
            border: "none",
            background: "transparent",
            cursor: "pointer",
            color: notesTheme.muted,
            width: 16,
            height: 16,
            borderRadius: 3,
            padding: 0,
            marginLeft: 2
          }}
        >
          <i className="fa fa-xmark" style={{ fontSize: 10 }} />
        </button>
      )}
    </div>
  );
}
