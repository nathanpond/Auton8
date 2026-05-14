import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  CabinetDto,
  LocatorResponse,
  NoteDto,
  NoteKind,
  NotebookDto,
  PageDto,
  PageTreeNodeDto,
  ProjectDto,
  UpdateCabinetRequest,
  UpdateNotebookRequest,
  createCabinet,
  createNote,
  createNotebook,
  createPage,
  createProject,
  deleteCabinet,
  deleteNote,
  deleteNotebook,
  deletePage,
  fetchPage,
  fetchPageTree,
  listCabinetsPage,
  listNotebooksPage,
  listNotes,
  listProjectsPage,
  resolveLocator,
  updateCabinet,
  updateNote,
  updateNotebook,
  updatePage
} from "@/api/content";

export const PROJECTS_QUERY_KEY = ["content", "projects"] as const;
export const locatorKey = (locator: number) => ["content", "locator", locator] as const;

export function useLocator(locator: number | null) {
  return useQuery<LocatorResponse | null>({
    queryKey: locator == null ? ["content", "locator", "none"] : locatorKey(locator),
    enabled: locator != null,
    queryFn: ({ signal }) => resolveLocator(locator!, signal),
    // Locators don't change once issued, so cache aggressively. The ancestor
    // chain CAN change if an entity is moved between parents — we accept a
    // small staleness window in exchange for skipping a network hop on
    // every navigation back to the same locator within the session.
    staleTime: 5 * 60_000
  });
}
export const cabinetsKey = (projectId: string) =>
  ["content", "cabinets", projectId] as const;
export const notebooksKey = (cabinetId: string) =>
  ["content", "notebooks", cabinetId] as const;
export const pageTreeKey = (notebookId: string) =>
  ["content", "page-tree", notebookId] as const;
export const pageKey = (pageId: string) => ["content", "page", pageId] as const;
export const notesKey = (pageId: string) => ["content", "notes", pageId] as const;

export function useProjects() {
  return useQuery<ProjectDto[]>({
    queryKey: PROJECTS_QUERY_KEY,
    queryFn: async ({ signal }) => {
      const result = await listProjectsPage({ pageSize: 200 }, signal);
      return result.items;
    }
  });
}

export function useCreateProject() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { name: string; description?: string }) =>
      createProject(vars.name, vars.description),
    onSuccess: () => qc.invalidateQueries({ queryKey: PROJECTS_QUERY_KEY })
  });
}

export function useCabinets(projectId: string | null) {
  return useQuery<CabinetDto[]>({
    queryKey: projectId ? cabinetsKey(projectId) : ["content", "cabinets", "none"],
    enabled: !!projectId,
    queryFn: async ({ signal }) => {
      const result = await listCabinetsPage(projectId!, signal);
      return result.items;
    }
  });
}

export function useCreateCabinet(projectId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { name: string; description?: string; icon?: string }) =>
      createCabinet({ projectId: projectId!, ...vars }),
    onSuccess: () => {
      if (projectId) qc.invalidateQueries({ queryKey: cabinetsKey(projectId) });
    }
  });
}

export function useUpdateCabinet(projectId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { id: string; body: UpdateCabinetRequest }) =>
      updateCabinet(vars.id, vars.body),
    onSuccess: (cabinet) => {
      qc.invalidateQueries({ queryKey: cabinetsKey(cabinet.projectId) });
      if (projectId && projectId !== cabinet.projectId) {
        // Move: invalidate the origin project too so the cabinet disappears
        // from its old rail.
        qc.invalidateQueries({ queryKey: cabinetsKey(projectId) });
      }
    }
  });
}

export function useDeleteCabinet(projectId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteCabinet(id),
    onSuccess: () => {
      if (projectId) qc.invalidateQueries({ queryKey: cabinetsKey(projectId) });
    }
  });
}

export function useNotebooks(cabinetId: string | null) {
  return useQuery({
    queryKey: cabinetId ? notebooksKey(cabinetId) : ["content", "notebooks", "none"],
    enabled: !!cabinetId,
    queryFn: async ({ signal }) => {
      const result = await listNotebooksPage(cabinetId!, signal);
      return result.items;
    }
  });
}

export function useCreateNotebook(cabinetId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { name: string; description?: string; icon?: string }) =>
      createNotebook({ cabinetId: cabinetId!, ...vars }),
    onSuccess: () => {
      if (cabinetId) qc.invalidateQueries({ queryKey: notebooksKey(cabinetId) });
    }
  });
}

export function useUpdateNotebook(cabinetId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { id: string; body: UpdateNotebookRequest }) =>
      updateNotebook(vars.id, vars.body),
    onSuccess: (notebook: NotebookDto) => {
      qc.invalidateQueries({ queryKey: notebooksKey(notebook.cabinetId) });
      if (cabinetId && cabinetId !== notebook.cabinetId) {
        // Move: also invalidate the source cabinet so the row disappears
        // from its old list.
        qc.invalidateQueries({ queryKey: notebooksKey(cabinetId) });
      }
    }
  });
}

export function useDeleteNotebook(cabinetId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteNotebook(id),
    onSuccess: () => {
      if (cabinetId) qc.invalidateQueries({ queryKey: notebooksKey(cabinetId) });
    }
  });
}

export function usePageTree(notebookId: string | null) {
  return useQuery<PageTreeNodeDto[]>({
    queryKey: notebookId ? pageTreeKey(notebookId) : ["content", "page-tree", "none"],
    enabled: !!notebookId,
    queryFn: ({ signal }) => fetchPageTree(notebookId!, signal)
  });
}

export function usePage(pageId: string | null) {
  return useQuery<PageDto>({
    queryKey: pageId ? pageKey(pageId) : ["content", "page", "none"],
    enabled: !!pageId,
    queryFn: ({ signal }) => fetchPage(pageId!, signal)
  });
}

export function useCreatePage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: {
      notebookId: string;
      parentPageId?: string | null;
      title: string;
      bodyJsonb?: string;
    }) => createPage(vars),
    onSuccess: (page) => {
      // Invalidate the destination notebook's page tree. Tracking which other
      // cached trees might transitively change (none, since pages can't move
      // notebooks via create) isn't worth the extra cache reads.
      qc.invalidateQueries({ queryKey: pageTreeKey(page.notebookId) });
    }
  });
}

export function useUpdatePage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { id: string; body: Parameters<typeof updatePage>[1] }) =>
      updatePage(vars.id, vars.body),
    onSuccess: (page) => {
      qc.invalidateQueries({ queryKey: pageKey(page.id) });
      qc.invalidateQueries({ queryKey: pageTreeKey(page.notebookId) });
    }
  });
}

export function useDeletePage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deletePage(id),
    onSuccess: () => {
      // Invalidate every cached page-tree — a deleted page might have lived
      // in any notebook's tree, and tracking which tree contained it would
      // require an extra cache read. Cheap to nuke them all.
      qc.invalidateQueries({ queryKey: ["content", "page-tree"] });
    }
  });
}

export function useNotes(pageId: string | null) {
  return useQuery<NoteDto[]>({
    queryKey: pageId ? notesKey(pageId) : ["content", "notes", "none"],
    enabled: !!pageId,
    queryFn: ({ signal }) => listNotes(pageId!, signal)
  });
}

export function useCreateNote(pageId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { noteKind: NoteKind; title?: string; contentJsonb?: string }) =>
      createNote(pageId!, vars),
    onSuccess: () => {
      if (pageId) qc.invalidateQueries({ queryKey: notesKey(pageId) });
    }
  });
}

export function useUpdateNote(pageId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: {
      id: string;
      body: { title?: string; contentJsonb?: string; sortOrder?: number };
    }) => updateNote(vars.id, vars.body),
    onSuccess: () => {
      if (pageId) qc.invalidateQueries({ queryKey: notesKey(pageId) });
    }
  });
}

export function useDeleteNote(pageId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteNote(id),
    onSuccess: () => {
      if (pageId) qc.invalidateQueries({ queryKey: notesKey(pageId) });
    }
  });
}
