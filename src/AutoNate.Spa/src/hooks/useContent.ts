import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  CabinetDto,
  LocatorResponse,
  NoteDto,
  NoteKind,
  NoteVersionDetail,
  NoteVersionsResponse,
  NotebookDto,
  PageDto,
  PageTreeNodeDto,
  PageVersionDetail,
  PageVersionsResponse,
  ProjectDto,
  ProjectMembersResponse,
  ProjectRoleWire,
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
  favoritePage,
  fetchNoteVersion,
  fetchPage,
  fetchPageTree,
  fetchPageVersion,
  listNoteVersions,
  listPageVersions,
  listProjectMembers,
  removeProjectMember,
  revokeDerivedGrant,
  restoreNoteVersion,
  restorePageVersion,
  setProjectMemberRole,
  unfavoritePage,
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
export const pageVersionsKey = (pageId: string) =>
  ["content", "page-versions", pageId] as const;
export const pageVersionKey = (pageId: string, versionNumber: number) =>
  ["content", "page-version", pageId, versionNumber] as const;
export const noteVersionsKey = (noteId: string) =>
  ["content", "note-versions", noteId] as const;
export const noteVersionKey = (noteId: string, versionNumber: number) =>
  ["content", "note-version", noteId, versionNumber] as const;

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

// Toggles the current user's favorite flag on a page. Optimistically flips
// the cached PageDto's isFavorited so the star icon swaps state instantly;
// on error the previous value is restored.
export function useToggleFavoritePage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { id: string; favorited: boolean }) =>
      vars.favorited ? favoritePage(vars.id) : unfavoritePage(vars.id),
    onMutate: async (vars) => {
      await qc.cancelQueries({ queryKey: pageKey(vars.id) });
      const previous = qc.getQueryData<PageDto>(pageKey(vars.id));
      if (previous) {
        qc.setQueryData<PageDto>(pageKey(vars.id), {
          ...previous,
          isFavorited: vars.favorited
        });
      }
      return { previous };
    },
    onError: (_err, vars, ctx) => {
      if (ctx?.previous) qc.setQueryData(pageKey(vars.id), ctx.previous);
    },
    onSettled: (_data, _err, vars) => {
      qc.invalidateQueries({ queryKey: pageKey(vars.id) });
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

// ── Version history ───────────────────────────────────────────────────────

export function usePageVersions(pageId: string | null, enabled = true) {
  return useQuery<PageVersionsResponse>({
    queryKey: pageId ? pageVersionsKey(pageId) : ["content", "page-versions", "none"],
    enabled: !!pageId && enabled,
    queryFn: ({ signal }) => listPageVersions(pageId!, { pageSize: 100 }, signal)
  });
}

export function usePageVersion(pageId: string | null, versionNumber: number | null) {
  return useQuery<PageVersionDetail>({
    queryKey:
      pageId && versionNumber != null
        ? pageVersionKey(pageId, versionNumber)
        : ["content", "page-version", "none"],
    enabled: !!pageId && versionNumber != null,
    queryFn: ({ signal }) => fetchPageVersion(pageId!, versionNumber!, signal),
    // Historical revision bodies are immutable so we can cache forever.
    staleTime: Infinity
  });
}

// Restores a page version. After the server creates the snapshot + replaces
// current, the cached page + tree + versions list all need refreshing so the
// editor reads the new current body and the history modal shows the new
// kind='restore' entry.
export function useRestorePageVersion() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { pageId: string; versionNumber: number; note?: string }) =>
      restorePageVersion(vars.pageId, vars.versionNumber, vars.note),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: pageKey(vars.pageId) });
      qc.invalidateQueries({ queryKey: pageVersionsKey(vars.pageId) });
      qc.invalidateQueries({ queryKey: ["content", "page-tree"] });
    }
  });
}

export function useNoteVersions(noteId: string | null, enabled = true) {
  return useQuery<NoteVersionsResponse>({
    queryKey: noteId ? noteVersionsKey(noteId) : ["content", "note-versions", "none"],
    enabled: !!noteId && enabled,
    queryFn: ({ signal }) => listNoteVersions(noteId!, { pageSize: 100 }, signal)
  });
}

export function useNoteVersion(noteId: string | null, versionNumber: number | null) {
  return useQuery<NoteVersionDetail>({
    queryKey:
      noteId && versionNumber != null
        ? noteVersionKey(noteId, versionNumber)
        : ["content", "note-version", "none"],
    enabled: !!noteId && versionNumber != null,
    queryFn: ({ signal }) => fetchNoteVersion(noteId!, versionNumber!, signal),
    staleTime: Infinity
  });
}

export function useRestoreNoteVersion(pageId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { noteId: string; versionNumber: number; note?: string }) =>
      restoreNoteVersion(vars.noteId, vars.versionNumber, vars.note),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: noteVersionsKey(vars.noteId) });
      if (pageId) qc.invalidateQueries({ queryKey: notesKey(pageId) });
    }
  });
}

export const projectMembersKey = (projectId: string) =>
  ["content", "project-members", projectId] as const;

export function useProjectMembers(projectId: string | null) {
  return useQuery<ProjectMembersResponse>({
    queryKey: projectId ? projectMembersKey(projectId) : ["content", "project-members", "none"],
    enabled: !!projectId,
    queryFn: ({ signal }) => listProjectMembers(projectId!, signal)
  });
}

export function useSetProjectMemberRole(projectId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { userId: string; role: ProjectRoleWire }) =>
      setProjectMemberRole(projectId!, vars.userId, vars.role),
    onSuccess: () => {
      if (projectId) qc.invalidateQueries({ queryKey: projectMembersKey(projectId) });
    }
  });
}

export function useRemoveProjectMember(projectId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (userId: string) => removeProjectMember(projectId!, userId),
    onSuccess: () => {
      if (projectId) qc.invalidateQueries({ queryKey: projectMembersKey(projectId) });
    }
  });
}

export function useRevokeDerivedGrant(projectId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (grantId: string) => revokeDerivedGrant(projectId!, grantId),
    onSuccess: () => {
      if (projectId) qc.invalidateQueries({ queryKey: projectMembersKey(projectId) });
    }
  });
}
