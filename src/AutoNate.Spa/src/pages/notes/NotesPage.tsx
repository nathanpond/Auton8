import { useEffect, useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useQueries } from "@tanstack/react-query";
import {
  CabinetDto,
  NotebookDto,
  PageTreeNodeDto,
  fetchPageTree
} from "@/api/content";
import {
  pageTreeKey,
  useCabinets,
  useCreateCabinet,
  useCreateNote,
  useCreateNotebook,
  useCreatePage,
  useDeleteCabinet,
  useDeleteNotebook,
  useDeletePage,
  useLocator,
  useNotes,
  useNotebooks,
  usePage,
  useProjects,
  useUpdateCabinet,
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
import { EditorTab, NotebookWithPages, flattenToTree, tabsForPage } from "./types";
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
  const params = useParams<{ locator?: string }>();
  const navigate = useNavigate();
  const urlLocator = parseLocator(params.locator);
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
  const [newPageTarget, setNewPageTarget] = useState<NewPageTarget | null>(null);
  // Ids of nodes the user has asked to expand (e.g. after creating a child
  // inside them). Stays additive — manual collapses are preserved because
  // PageRow / NotebookRow only force-open when their id is in this set.
  const [forceExpandIds, setForceExpandIds] = useState<ReadonlySet<string>>(new Set());

  // Default-select the first project once it loads (or if the URL-supplied
  // project id is no longer in the user's accessible set — silently fall
  // through to the first instead of leaving the rail empty).
  useEffect(() => {
    if (projects.length === 0) return;
    if (!activeProjectId || !projects.find((p) => p.id === activeProjectId)) {
      setActiveProjectId(projects[0].id);
    }
  }, [projects, activeProjectId]);

  const cabinetsQuery = useCabinets(activeProjectId);
  const cabinets = cabinetsQuery.data ?? [];

  // Default-select the first cabinet whenever the project / cabinet list
  // changes — otherwise the rail has nothing highlighted.
  useEffect(() => {
    if (!cabinets.length) {
      setActiveCabinetId(null);
      return;
    }
    if (!activeCabinetId || !cabinets.find((c) => c.id === activeCabinetId)) {
      setActiveCabinetId(cabinets[0].id);
    }
  }, [cabinets, activeCabinetId]);

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

  const pageQuery = usePage(activePageId);
  const notesQuery = useNotes(activePageId);

  // Sync tab state when the active page or its notes list changes.
  useEffect(() => {
    if (!activePageId || !pageQuery.data || !notesQuery.data) return;
    setTabsByPage((prev) => {
      const tabs = tabsForPage(activePageId, pageQuery.data!.title, notesQuery.data!);
      const existing = prev[activePageId];
      const activeTabId =
        existing?.activeTabId && tabs.find((t) => t.id === existing.activeTabId)
          ? existing.activeTabId
          : tabs[0]?.id;
      return { ...prev, [activePageId]: { tabs, activeTabId: activeTabId ?? "" } };
    });
  }, [activePageId, pageQuery.data, notesQuery.data]);

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

  // Deepest available locator becomes the URL. Preference order is the same
  // as the entity hierarchy: page > notebook > cabinet > project. Using
  // `replace` so every click doesn't push a history entry — refresh restores
  // exactly where you were, back-button still navigates between SPA routes.
  const deepestLocator = useMemo(() => {
    if (activePageTreeNode) return activePageTreeNode.locator;
    if (activeNotebook) return activeNotebook.locator;
    if (activeCabinet) return activeCabinet.locator;
    if (activeProject) return activeProject.locator;
    return null;
  }, [activePageTreeNode, activeNotebook, activeCabinet, activeProject]);

  useEffect(() => {
    if (deepestLocator == null) return;
    if (urlLocator === deepestLocator) return;
    navigate(`/notes/${deepestLocator}`, { replace: true });
  }, [deepestLocator, urlLocator, navigate]);

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

  const onCloseTab = (tabId: string) => {
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
        width: "100%",
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
