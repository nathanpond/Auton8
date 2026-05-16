import { useEffect, useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useQueries, useQueryClient } from "@tanstack/react-query";
import {
  CabinetDto,
  NotebookDto,
  NoteDto,
  PageTreeNodeDto,
  fetchPageTree
} from "@/api/content";
import {
  notesKey,
  pageTreeKey,
  useCabinets,
  useCreateCabinet,
  useCreateNote,
  useCreateNotebook,
  useCreatePage,
  useDeleteCabinet,
  useDeleteNote,
  useDeleteNotebook,
  useDeletePage,
  useLocator,
  useNotes,
  useNotebooks,
  usePage,
  useProjects,
  useUpdateCabinet,
  useUpdateNote,
  useUpdateNotebook,
  useUpdatePage
} from "@/hooks/useContent";
import { ProjectSelector } from "./ProjectSelector";
import { CabinetRail } from "./CabinetRail";
import { ConfirmDialog } from "./ConfirmDialog";
import { Explorer, NewPageTarget } from "./Explorer";
import { EditorPane } from "./EditorPane";
import { EditCabinetModal } from "./EditCabinetModal";
import { EditNotebookModal } from "./EditNotebookModal";
import { EditPageModal } from "./EditPageModal";
import { NewCabinetModal } from "./NewCabinetModal";
import { NewNotebookModal } from "./NewNotebookModal";
import { NewNoteModal } from "./NewNoteModal";
import { NewPageModal } from "./NewPageModal";
import { ProjectSettingsModal } from "./ProjectSettingsModal";
import { EditorTab, NotebookWithPages, flattenToTree, tabsForPage } from "./types";
import { useYjsNotesList } from "@/lib/yjs/useYjsNotesList";
import { WireNoteKind, notesTheme } from "./notesTheme";
import "./notes.css";

// Notes page — the SPA entry point at /notes. Lives inside <AppShell.Main>
// and fills its viewport (the route uses the .app-shell-content-edge cancel
// to bleed flush past the shell's default 1.5rem padding). The internal
// layout owns its own sidebar: project picker on top of a color-tile cabinet
// rail + a notebook/page explorer, with a tab-strip editor pane on the right.
export default function NotesPage() {
  // URL-backed selection: /notes/{locator} — a single numeric locator
  // identifies whatever the user has open (project / cabinet / notebook /
  // page / note). On cold-load we hit /api/content/locator/{n} once to
  // hydrate the full ancestor chain; from there the SPA owns the state.
  // Single splat route (`notes/*`) so NotesPage stays mounted across every
  // segment count. We split `params["*"]` into [locator, noteIndex] manually.
  const params = useParams();
  const navigate = useNavigate();
  const urlSegments = useMemo(
    () => (params["*"] ?? "").split("/").filter(Boolean),
    [params]
  );
  const urlLocator = parseLocator(urlSegments[0]);
  // Per-page note index from the second URL segment. Silently ignored if the
  // first segment doesn't resolve to a page or no note in the page matches.
  const urlNoteIndex = parseLocator(urlSegments[1]);
  const locatorQuery = useLocator(urlLocator);
  const resolved = locatorQuery.data ?? null;

  const projectsQuery = useProjects();
  const projects = projectsQuery.data ?? [];

  const [activeProjectId, setActiveProjectId] = useState<string | null>(null);
  const [activeCabinetId, setActiveCabinetId] = useState<string | null>(null);
  const [activePageId, setActivePageId] = useState<string | null>(null);

  // Hydrate selection from the resolved locator response — fires once per
  // distinct URL locator. The ancestor chain may contain any subset; we
  // populate whichever fields are present, falling through to the default-
  // select effects for anything we couldn't pin from the URL.
  useEffect(() => {
    if (!resolved) return;
    if (resolved.ancestors.project) {
      setActiveProjectId(resolved.ancestors.project.id);
    }
    if (resolved.ancestors.cabinet) {
      setActiveCabinetId(resolved.ancestors.cabinet.id);
    }
    if (resolved.ancestors.page) {
      setActivePageId(resolved.ancestors.page.id);
    } else if (resolved.kind === "page") {
      setActivePageId(resolved.id);
    }
  }, [resolved]);
  const [tabsByPage, setTabsByPage] = useState<
    Record<string, { tabs: EditorTab[]; activeTabId: string }>
  >({});
  const [modalOpen, setModalOpen] = useState(false);
  const [cabinetModalOpen, setCabinetModalOpen] = useState(false);
  const [editCabinet, setEditCabinet] = useState<CabinetDto | null>(null);
  const [deleteCabinet, setDeleteCabinet] = useState<CabinetDto | null>(null);
  const [deleteCabinetError, setDeleteCabinetError] = useState<string | null>(null);
  const [notebookModalOpen, setNotebookModalOpen] = useState(false);
  const [editNotebook, setEditNotebook] = useState<NotebookDto | null>(null);
  const [deleteNotebook, setDeleteNotebook] = useState<NotebookDto | null>(null);
  const [deleteNotebookError, setDeleteNotebookError] = useState<string | null>(null);
  const [editPage, setEditPage] = useState<PageTreeNodeDto | null>(null);
  const [deletePageNode, setDeletePageNode] = useState<PageTreeNodeDto | null>(null);
  const [deletePageError, setDeletePageError] = useState<string | null>(null);
  // Confirm-then-delete target for the tab strip's close (×) button.
  // Stores the tab id (so we can remove it from tabs state on success) and
  // the resolved note id + display name (powering the mutation + dialog).
  const [deleteNoteTab, setDeleteNoteTab] = useState<{
    tabId: string;
    noteId: string;
    noteName: string;
  } | null>(null);
  const [deleteNoteError, setDeleteNoteError] = useState<string | null>(null);
  const [newPageTarget, setNewPageTarget] = useState<NewPageTarget | null>(null);
  const [projectSettingsOpen, setProjectSettingsOpen] = useState(false);
  // Ids of nodes the user has asked to expand (e.g. after creating a child
  // inside them). Stays additive — manual collapses are preserved because
  // PageRow / NotebookRow only force-open when their id is in this set.
  const [forceExpandIds, setForceExpandIds] = useState<ReadonlySet<string>>(new Set());

  // When the URL has a locator we haven't resolved yet, the default-select
  // and URL-writeback effects must hold off — otherwise the queries that
  // arrive before the resolve (projects, then cabinets) trigger
  // setActiveProjectId/setActiveCabinetId on stale state, the URL writeback
  // fires with the wrong locator, and the page selection from the URL is
  // overwritten before the resolve even completes.
  const urlPending = urlLocator != null && !resolved && !locatorQuery.isError;

  // Default-select the first project once it loads (or if the URL-supplied
  // project id is no longer in the user's accessible set — silently fall
  // through to the first instead of leaving the rail empty).
  useEffect(() => {
    if (urlPending) return;
    if (projects.length === 0) return;
    if (!activeProjectId || !projects.find((p) => p.id === activeProjectId)) {
      setActiveProjectId(projects[0].id);
    }
  }, [projects, activeProjectId, urlPending]);

  const cabinetsQuery = useCabinets(activeProjectId);
  const cabinets = cabinetsQuery.data ?? [];

  // Default-select the first cabinet whenever the project / cabinet list
  // changes — otherwise the rail has nothing highlighted.
  useEffect(() => {
    if (urlPending) return;
    if (!cabinets.length) {
      setActiveCabinetId(null);
      return;
    }
    if (!activeCabinetId || !cabinets.find((c) => c.id === activeCabinetId)) {
      setActiveCabinetId(cabinets[0].id);
    }
  }, [cabinets, activeCabinetId, urlPending]);

  const notebooksQuery = useNotebooks(activeCabinetId);
  const notebooks = notebooksQuery.data ?? [];

  // Fetch page trees for every notebook in the active cabinet in parallel.
  // We used to only fetch the first notebook's tree, which broke URL-restore
  // when the page being restored lived in a different notebook (the tree it
  // belonged to never loaded, so the page row never appeared in the explorer
  // and the editor pane couldn't resolve its containing notebook).
  const pageTreeQueries = useQueries({
    queries: notebooks.map((nb) => ({
      queryKey: pageTreeKey(nb.id),
      queryFn: ({ signal }: { signal?: AbortSignal }) => fetchPageTree(nb.id, signal)
    }))
  });

  const notebooksWithPages: NotebookWithPages[] = useMemo(() => {
    return notebooks.map((nb, idx) => ({
      ...nb,
      pages: flattenToTree(pageTreeQueries[idx]?.data ?? [])
    }));
  }, [notebooks, pageTreeQueries]);

  const anyTreeLoading = pageTreeQueries.some((q) => q.isLoading);

  const qc = useQueryClient();
  const pageQuery = usePage(activePageId);
  const notesQuery = useNotes(activePageId);

  // Live notes list — a Y.Doc per page mirrors the `notes` table so users
  // viewing the same page see new tabs appear as soon as another user
  // creates a note. The REST data is passed in as a seed; editors push
  // any REST entries the Y.Map is missing.
  const notesYjs = useYjsNotesList(activePageId, notesQuery.data);

  // Back-propagate Yjs metadata into React-Query so consumers that still
  // read `notesQuery.data` (EditorPane → activeNote → EditableNoteTitle)
  // pick up cross-user title renames. We match by id and only overwrite
  // when the Y.Map's updatedAtUtc is strictly newer than what's in cache
  // — guards against an in-flight REST refetch clobbering a fresh Yjs
  // edit. ContentJsonb and other DTO fields not in pagemeta are left
  // untouched (the note's body lives in a different Y.Doc).
  useEffect(() => {
    if (!activePageId || notesYjs.notes.length === 0) return;
    qc.setQueryData<NoteDto[]>(notesKey(activePageId), (prev) => {
      if (!prev) return prev;
      let mutated = false;
      const next = prev.map((existing) => {
        const yEntry = notesYjs.notes.find((n) => n.id === existing.id);
        if (!yEntry) return existing;
        if (yEntry.updatedAtUtc <= existing.updatedAtUtc) return existing;
        mutated = true;
        return {
          ...existing,
          title: yEntry.title,
          sortOrder: yEntry.sortOrder,
          pageNoteIndex: yEntry.pageNoteIndex,
          isArchived: yEntry.isArchived,
          updatedAtUtc: yEntry.updatedAtUtc,
          updatedBy: yEntry.updatedBy
        };
      });
      return mutated ? next : prev;
    });
  }, [activePageId, notesYjs.notes, qc]);

  // Cold-start fallback: before the pagemeta Y.Doc has been seeded for
  // the first time, show REST data so the tab strip renders immediately.
  // After seed (one tick later), notesYjs.notes takes over.
  const liveNotes =
    notesYjs.notes.length > 0 ? notesYjs.notes : (notesQuery.data ?? []);

  // Sync tab state when the active page or its notes list changes. When the
  // URL specifies a note index, prefer that note as the default active tab —
  // refreshes on /notes/{page}/{note} land directly on the note tab. If the
  // index doesn't match any note we silently fall through to the page tab.
  useEffect(() => {
    if (!activePageId || !pageQuery.data) return;
    setTabsByPage((prev) => {
      const tabs = tabsForPage(activePageId, pageQuery.data!.title, liveNotes);
      const existing = prev[activePageId];
      let activeTabId: string | undefined;
      // Existing user-selected tab wins if still valid (don't yank the user
      // back to the URL's tab when they've already clicked elsewhere).
      if (existing?.activeTabId && tabs.find((t) => t.id === existing.activeTabId)) {
        activeTabId = existing.activeTabId;
      }
      // First-time hydration: honor the URL's note index if provided.
      if (!activeTabId && urlNoteIndex != null) {
        const match = liveNotes.find((n) => n.pageNoteIndex === urlNoteIndex);
        if (match) {
          activeTabId = `${activePageId}::${match.id}`;
        }
      }
      activeTabId ??= tabs[0]?.id;
      return { ...prev, [activePageId]: { tabs, activeTabId: activeTabId ?? "" } };
    });
  }, [activePageId, pageQuery.data, liveNotes, urlNoteIndex]);

  const createCabinet = useCreateCabinet(activeProjectId);
  const updateCabinetMutation = useUpdateCabinet(activeProjectId);
  const deleteCabinetMutation = useDeleteCabinet(activeProjectId);
  const createNotebook = useCreateNotebook(activeCabinetId);
  const updateNotebookMutation = useUpdateNotebook(activeCabinetId);
  const deleteNotebookMutation = useDeleteNotebook(activeCabinetId);
  const updatePageMutation = useUpdatePage();
  const deletePageMutation = useDeletePage();
  const createPageMutation = useCreatePage();
  const createNote = useCreateNote(activePageId);
  const deleteNoteMutation = useDeleteNote(activePageId);
  const updateNoteMutation = useUpdateNote(activePageId);

  // Drag-to-reorder note tabs. Persist new sortOrder via PATCH for every
  // note whose position changed; updateNote.onSuccess invalidates the
  // notes query, which feeds the newer-wins seed in useYjsNotesList,
  // which broadcasts the new sortOrder to other connected users.
  const onReorderNotes = (orderedNoteIds: string[]) => {
    if (!activePageId) return;
    orderedNoteIds.forEach((noteId, idx) => {
      const existing = liveNotes.find((n) => n.id === noteId);
      // Skip notes whose sortOrder didn't change — saves a PATCH per
      // unchanged tab when the user drags one tab a single position.
      if (existing && existing.sortOrder === idx) return;
      updateNoteMutation.mutate({ id: noteId, body: { sortOrder: idx } });
    });
  };

  const activeCabinet = cabinets.find((c) => c.id === activeCabinetId) ?? null;
  const activeProject = projects.find((p) => p.id === activeProjectId) ?? null;
  const activeNotebook =
    notebooksWithPages.find((nb) =>
      nb.pages.some((p) => containsPage(p, activePageId))
    ) ?? null;
  const activePageTreeNode = activeNotebook
    ? findInTree(activeNotebook.pages, activePageId)
    : null;
  const pageState = activePageId ? tabsByPage[activePageId] : undefined;

  // Active note's pageNoteIndex drives the second URL segment. Computed from
  // the active tab id (a string like `${pageId}::${noteId}` or
  // `${pageId}::page`) and the loaded notes list. Memoised on the SCALAR
  // inputs (activeTabId + notes data ref) so the value's reference stays
  // stable across renders that don't actually change it — without that, the
  // URL writeback effect re-fires every render and downstream Mantine
  // components see new prop refs, which has been observed to trip
  // `useMergedRef` setState loops.
  const activeNoteId = useMemo(() => {
    if (!pageState?.activeTabId) return null;
    const sep = "::";
    const idx = pageState.activeTabId.indexOf(sep);
    if (idx < 0) return null;
    const tail = pageState.activeTabId.slice(idx + sep.length);
    return tail === "page" ? null : tail;
  }, [pageState?.activeTabId]);

  const activeNotePageIndex = useMemo(() => {
    if (!activeNoteId) return null;
    return liveNotes.find((n) => n.id === activeNoteId)?.pageNoteIndex ?? null;
  }, [activeNoteId, liveNotes]);

  // Desired URL: /notes/{deepestLocator}[/{pageNoteIndex}]. Page tab or no
  // note → single segment. Note tab → two segments. Defined as a string so
  // the writeback can compare against the current location verbatim.
  const desiredPath = useMemo(() => {
    let base: string | null = null;
    if (activePageTreeNode) base = `/notes/${activePageTreeNode.locator}`;
    else if (activeNotebook) base = `/notes/${activeNotebook.locator}`;
    else if (activeCabinet) base = `/notes/${activeCabinet.locator}`;
    else if (activeProject) base = `/notes/${activeProject.locator}`;
    if (base && activePageTreeNode && activeNotePageIndex != null) {
      base = `${base}/${activeNotePageIndex}`;
    }
    return base;
  }, [
    activePageTreeNode,
    activeNotebook,
    activeCabinet,
    activeProject,
    activeNotePageIndex
  ]);

  const currentPath = useMemo(() => {
    if (urlLocator == null) return null;
    return urlNoteIndex != null
      ? `/notes/${urlLocator}/${urlNoteIndex}`
      : `/notes/${urlLocator}`;
  }, [urlLocator, urlNoteIndex]);

  useEffect(() => {
    // Wait until the URL's locator has resolved before touching the URL.
    if (urlPending) return;
    // Page in flight: activePageId is set but its tree node hasn't loaded
    // yet (page-tree query still pending). Without this gate the URL would
    // briefly overwrite a /notes/{page} URL with /notes/{cabinet} during
    // the load window. Hits both cold-load and same-cabinet navigation.
    if (activePageId && !activePageTreeNode) return;
    // Notes for the active page haven't loaded yet — hold off so the
    // writeback can't drop the /{noteIndex} segment briefly while the
    // notes query settles. activeTabId would default to the page tab in
    // that window otherwise, and we'd write /notes/{page} over the
    // /notes/{page}/{n} URL the user came in on.
    if (activePageId && notesQuery.data === undefined) return;
    if (desiredPath == null) return;
    if (desiredPath === currentPath) return;
    navigate(desiredPath, { replace: true });
  }, [
    desiredPath,
    currentPath,
    navigate,
    urlPending,
    activePageId,
    activePageTreeNode,
    notesQuery.data
  ]);

  const onPagePick = (pageId: string) => {
    setActivePageId(pageId);
  };

  const onSwitchTab = (tabId: string) => {
    if (!activePageId) return;
    setTabsByPage((prev) => {
      const cur = prev[activePageId];
      if (!cur) return prev;
      return { ...prev, [activePageId]: { ...cur, activeTabId: tabId } };
    });
  };

  // Closing a note tab deletes the underlying note (no "close without
  // delete" — closing == deleting per design). Always confirms first so
  // users don't lose work to a misclick on the small × button. Page tabs
  // can't be closed (Tab renders no × for them), so we only ever land here
  // for note tabs and just guard defensively.
  const onCloseTab = (tabId: string) => {
    if (!activePageId) return;
    const cur = tabsByPage[activePageId];
    const tab = cur?.tabs.find((t) => t.id === tabId);
    if (!tab || tab.kind === "page") return;
    const noteId = tab.noteId;
    if (!noteId) return;
    setDeleteNoteTab({ tabId, noteId, noteName: tab.name });
    setDeleteNoteError(null);
  };

  // Remove a tab from local state and pick a successor active tab if the
  // closed one was active. Called from the confirm-dialog onConfirm AFTER
  // the server delete succeeds (so the server is authoritative — we never
  // remove the tab on a failed delete).
  const removeTabLocally = (tabId: string) => {
    if (!activePageId) return;
    setTabsByPage((prev) => {
      const cur = prev[activePageId];
      if (!cur) return prev;
      const tabs = cur.tabs.filter((t) => t.id !== tabId);
      const activeTabId =
        cur.activeTabId === tabId ? tabs[0]?.id ?? "" : cur.activeTabId;
      return { ...prev, [activePageId]: { tabs, activeTabId } };
    });
  };

  const onCreateNote = async (vars: { name: string; kind: WireNoteKind }) => {
    if (!activePageId) return;
    const note = await createNote.mutateAsync({
      noteKind: vars.kind,
      title: vars.name
    });
    // Mirror into the pagemeta Y.Doc so other connected users see the new
    // tab appear without needing to refetch. The REST mutation already
    // invalidates React-Query on this client; the Yjs write is the
    // cross-client signal.
    notesYjs.upsertNote(note);
    setModalOpen(false);
    setTabsByPage((prev) => {
      const cur = prev[activePageId] ?? { tabs: [], activeTabId: "" };
      const newTab: EditorTab = {
        id: `${activePageId}::${note.id}`,
        kind: note.noteKind,
        name: note.title ?? "Untitled",
        noteId: note.id
      };
      return {
        ...prev,
        [activePageId]: { tabs: [...cur.tabs, newTab], activeTabId: newTab.id }
      };
    });
  };

  return (
    <div
      className="app-shell-content-edge"
      style={{
        display: "flex",
        // No explicit width: the `.app-shell-content-edge` rule applies a
        // negative -1.5rem margin to cancel the shell's 24px padding. With
        // `width: 100%`, the element would be locked to the parent's
        // content width (1235px on a 1283px viewport) and the negative
        // margins would only shift its position — leaving a 48px gap on
        // the right. Letting width default to `auto` lets the negative
        // margins actually grow the box so it spans the full viewport.
        height: "calc(100vh - 56px)",
        background: "#fff",
        fontFamily: "'Open Sans', system-ui, sans-serif",
        fontSize: 12,
        color: notesTheme.dark
      }}
    >
      {/* Inner sidebar: project selector on top, then cabinet rail + explorer. */}
      <div
        style={{
          display: "flex",
          flexDirection: "column",
          borderRight: `1px solid ${notesTheme.border}`,
          background: "#fff",
          flexShrink: 0
        }}
      >
        <div
          style={{
            padding: "10px",
            borderBottom: `1px solid ${notesTheme.border}`,
            background: "#fafbfc",
            width: 64 + 264
          }}
        >
          {activeProject ? (
            <ProjectSelector
              projects={projects}
              project={activeProject}
              onPick={(p) => {
                setActiveProjectId(p.id);
                setActiveCabinetId(null);
                setActivePageId(null);
              }}
            />
          ) : (
            <div style={{ height: 36 }} />
          )}
        </div>

        <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
          <CabinetRail
            cabinets={cabinets}
            activeId={activeCabinetId}
            onPick={(id) => {
              setActiveCabinetId(id);
              setActivePageId(null);
            }}
            onNew={() => setCabinetModalOpen(true)}
            canCreate={!!activeProjectId}
            onOpenSettings={() => setProjectSettingsOpen(true)}
            canOpenSettings={!!activeProject}
          />
          <Explorer
            cabinet={activeCabinet}
            notebooks={notebooksWithPages}
            loading={notebooksQuery.isLoading || anyTreeLoading}
            activePageId={activePageId}
            onPagePick={onPagePick}
            onNewNotebook={() => setNotebookModalOpen(true)}
            onRenameCabinet={setEditCabinet}
            onArchiveCabinet={(c) =>
              updateCabinetMutation.mutate({
                id: c.id,
                body: { isArchived: !c.isArchived }
              })
            }
            onDeleteCabinet={(c) => {
              setDeleteCabinetError(null);
              setDeleteCabinet(c);
            }}
            onRenameNotebook={setEditNotebook}
            onArchiveNotebook={(nb) =>
              updateNotebookMutation.mutate({
                id: nb.id,
                body: { isArchived: !nb.isArchived }
              })
            }
            onDeleteNotebook={(nb) => {
              setDeleteNotebookError(null);
              setDeleteNotebook(nb);
            }}
            onRenamePage={setEditPage}
            onArchivePage={(p) =>
              updatePageMutation.mutate({
                id: p.id,
                body: { isArchived: !p.isArchived }
              })
            }
            onDeletePage={(p) => {
              setDeletePageError(null);
              setDeletePageNode(p);
            }}
            onNewPage={setNewPageTarget}
            forceExpandIds={forceExpandIds}
          />
        </div>
      </div>

      <EditorPane
        page={pageQuery.data ?? null}
        pageNode={activePageTreeNode}
        cabinet={activeCabinet}
        notebook={activeNotebook}
        tabs={pageState?.tabs ?? []}
        activeTabId={pageState?.activeTabId ?? ""}
        notes={notesQuery.data ?? []}
        onSwitchTab={onSwitchTab}
        onCloseTab={onCloseTab}
        onNewNote={() => setModalOpen(true)}
        onReorderNotes={onReorderNotes}
      />

      {modalOpen && activePageId && (
        <NewNoteModal
          onClose={() => setModalOpen(false)}
          onCreate={onCreateNote}
          submitting={createNote.isPending}
        />
      )}

      {cabinetModalOpen && activeProjectId && (
        <NewCabinetModal
          onClose={() => setCabinetModalOpen(false)}
          onCreate={async (vars) => {
            const cabinet = await createCabinet.mutateAsync(vars);
            setCabinetModalOpen(false);
            setActiveCabinetId(cabinet.id);
            setActivePageId(null);
          }}
          submitting={createCabinet.isPending}
        />
      )}

      {notebookModalOpen && activeCabinetId && activeCabinet && (
        <NewNotebookModal
          cabinetName={activeCabinet.name}
          onClose={() => setNotebookModalOpen(false)}
          onCreate={async (vars) => {
            await createNotebook.mutateAsync(vars);
            setNotebookModalOpen(false);
            // Refreshed via the hook's onSuccess invalidation; the explorer
            // will pick up the new row once the query settles.
          }}
          submitting={createNotebook.isPending}
        />
      )}

      {editCabinet && (
        <EditCabinetModal
          cabinet={editCabinet}
          onClose={() => setEditCabinet(null)}
          onSave={async (vars) => {
            await updateCabinetMutation.mutateAsync({
              id: editCabinet.id,
              body: vars
            });
            setEditCabinet(null);
          }}
          submitting={updateCabinetMutation.isPending}
        />
      )}

      {deleteCabinet && (
        <ConfirmDialog
          icon="fa-trash"
          title={`Delete “${deleteCabinet.name}”?`}
          destructive
          body={
            <>
              This permanently deletes the cabinet and{" "}
              <strong>everything inside it</strong> — every notebook, page, note, and attachment.
              This cannot be undone.
            </>
          }
          confirmLabel="Delete cabinet"
          busy={deleteCabinetMutation.isPending}
          error={deleteCabinetError}
          onCancel={() => {
            if (deleteCabinetMutation.isPending) return;
            setDeleteCabinet(null);
            setDeleteCabinetError(null);
          }}
          onConfirm={async () => {
            try {
              await deleteCabinetMutation.mutateAsync(deleteCabinet.id);
              if (activeCabinetId === deleteCabinet.id) {
                setActiveCabinetId(null);
                setActivePageId(null);
              }
              setDeleteCabinet(null);
              setDeleteCabinetError(null);
            } catch (err) {
              setDeleteCabinetError(describeError(err));
            }
          }}
        />
      )}

      {editNotebook && (
        <EditNotebookModal
          notebook={editNotebook}
          onClose={() => setEditNotebook(null)}
          onSave={async (vars) => {
            await updateNotebookMutation.mutateAsync({
              id: editNotebook.id,
              body: vars
            });
            setEditNotebook(null);
          }}
          submitting={updateNotebookMutation.isPending}
        />
      )}

      {deleteNotebook && (
        <ConfirmDialog
          icon="fa-trash"
          title={`Delete “${deleteNotebook.name}”?`}
          destructive
          body={
            <>
              This permanently deletes the notebook and{" "}
              <strong>every page, note, and attachment inside it</strong>. This cannot be undone.
            </>
          }
          confirmLabel="Delete notebook"
          busy={deleteNotebookMutation.isPending}
          error={deleteNotebookError}
          onCancel={() => {
            if (deleteNotebookMutation.isPending) return;
            setDeleteNotebook(null);
            setDeleteNotebookError(null);
          }}
          onConfirm={async () => {
            try {
              await deleteNotebookMutation.mutateAsync(deleteNotebook.id);
              setDeleteNotebook(null);
              setDeleteNotebookError(null);
              // If the active page belonged to this notebook, clear selection.
              if (activeNotebook?.id === deleteNotebook.id) {
                setActivePageId(null);
              }
            } catch (err) {
              setDeleteNotebookError(describeError(err));
            }
          }}
        />
      )}

      {editPage && (
        <EditPageModal
          page={editPage}
          onClose={() => setEditPage(null)}
          onSave={async (vars) => {
            await updatePageMutation.mutateAsync({
              id: editPage.id,
              body: { title: vars.title }
            });
            setEditPage(null);
          }}
          submitting={updatePageMutation.isPending}
        />
      )}

      {deletePageNode && (
        <ConfirmDialog
          icon="fa-trash"
          title={`Delete “${deletePageNode.title}”?`}
          destructive
          body={
            <>
              This permanently deletes the page and{" "}
              <strong>every child page, note, and attachment inside it</strong>. This cannot be
              undone.
            </>
          }
          confirmLabel="Delete page"
          busy={deletePageMutation.isPending}
          error={deletePageError}
          onCancel={() => {
            if (deletePageMutation.isPending) return;
            setDeletePageNode(null);
            setDeletePageError(null);
          }}
          onConfirm={async () => {
            const target = deletePageNode;
            try {
              await deletePageMutation.mutateAsync(target.id);
              setDeletePageNode(null);
              setDeletePageError(null);
              // Clear selection if we deleted the active page (or one of its
              // ancestors — the cascade nukes descendants too).
              if (activePageId === target.id) {
                setActivePageId(null);
              }
            } catch (err) {
              setDeletePageError(describeError(err));
            }
          }}
        />
      )}

      {deleteNoteTab && (
        <ConfirmDialog
          icon="fa-trash"
          title={`Delete “${deleteNoteTab.noteName}”?`}
          destructive
          body={
            <>
              This permanently deletes the note and all of its content. This
              cannot be undone.
            </>
          }
          confirmLabel="Delete note"
          busy={deleteNoteMutation.isPending}
          error={deleteNoteError}
          onCancel={() => {
            if (deleteNoteMutation.isPending) return;
            setDeleteNoteTab(null);
            setDeleteNoteError(null);
          }}
          onConfirm={async () => {
            const target = deleteNoteTab;
            try {
              await deleteNoteMutation.mutateAsync(target.noteId);
              // Mirror the delete to pagemeta so other connected users'
              // tab strips drop this note. The local tab close happens
              // unconditionally next.
              notesYjs.removeNote(target.noteId);
              removeTabLocally(target.tabId);
              setDeleteNoteTab(null);
              setDeleteNoteError(null);
            } catch (err) {
              setDeleteNoteError(describeError(err));
            }
          }}
        />
      )}

      {newPageTarget && (
        <NewPageModal
          parentLabel={newPageTarget.parentLabel}
          parentKind={newPageTarget.parentKind}
          onClose={() => setNewPageTarget(null)}
          onCreate={async ({ title }) => {
            const target = newPageTarget;
            const page = await createPageMutation.mutateAsync({
              notebookId: target.notebookId,
              parentPageId: target.parentPageId,
              title
            });
            // Expand the parent so the new page is visible without a click,
            // then jump straight to it. The parent id is either the notebook
            // (top-level page) or the parent page (sub-page).
            const parentId = target.parentPageId ?? target.notebookId;
            setForceExpandIds((prev) => {
              const next = new Set(prev);
              next.add(parentId);
              return next;
            });
            setActivePageId(page.id);
            setNewPageTarget(null);
          }}
          submitting={createPageMutation.isPending}
        />
      )}

      <ProjectSettingsModal
        project={activeProject}
        opened={projectSettingsOpen}
        onClose={() => setProjectSettingsOpen(false)}
      />
    </div>
  );
}

function describeError(err: unknown): string {
  if (typeof err === "object" && err && "response" in err) {
    const resp = (err as { response?: { data?: { error?: string; message?: string } } }).response;
    return resp?.data?.error ?? resp?.data?.message ?? "Delete failed.";
  }
  return err instanceof Error ? err.message : "Delete failed.";
}

// /notes/:locator is optional; the param string is also untrusted — reject
// anything that isn't a positive integer instead of letting NaN poison the
// resolve query.
function parseLocator(raw: string | undefined): number | null {
  if (!raw) return null;
  const n = Number(raw);
  if (!Number.isFinite(n) || !Number.isInteger(n) || n <= 0) return null;
  return n;
}

function findInTree(
  pages: NotebookWithPages["pages"],
  pageId: string | null
): NotebookWithPages["pages"][number] | null {
  if (!pageId) return null;
  for (const p of pages) {
    if (p.id === pageId) return p;
    const inChildren = findInTree(p.children, pageId);
    if (inChildren) return inChildren;
  }
  return null;
}

function containsPage(
  page: NotebookWithPages["pages"][number],
  pageId: string | null
): boolean {
  if (!pageId) return false;
  if (page.id === pageId) return true;
  return page.children.some((c) => containsPage(c, pageId));
}
