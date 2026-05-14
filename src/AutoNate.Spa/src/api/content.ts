import { api } from "./client";

// DTOs mirror the records on the backend (see src/AutoNate.Web/Endpoints/*).
// snake_case JSON columns are deserialized by ASP.NET into camelCase by default
// so all fields below are camelCase TypeScript.

export type ProjectDto = {
  id: string;
  locator: number;
  name: string;
  description: string | null;
  deletionsLocked: boolean;
  isArchived: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  createdBy: string;
  updatedBy: string;
};

export type CabinetDto = {
  id: string;
  locator: number;
  projectId: string;
  name: string;
  description: string | null;
  icon: string | null;
  sortOrder: number;
  isArchived: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  createdBy: string;
  updatedBy: string;
};

export type NotebookDto = {
  id: string;
  locator: number;
  cabinetId: string;
  name: string;
  description: string | null;
  icon: string | null;
  sortOrder: number;
  isArchived: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  createdBy: string;
  updatedBy: string;
};

export type PageDto = {
  id: string;
  locator: number;
  notebookId: string;
  parentPageId: string | null;
  title: string;
  bodyJsonb: string;
  currentVersionNumber: number;
  sortOrder: number;
  isArchived: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  createdBy: string;
  updatedBy: string;
};

export type PageTreeNodeDto = {
  id: string;
  locator: number;
  notebookId: string;
  parentPageId: string | null;
  title: string;
  sortOrder: number;
  isArchived: boolean;
  currentVersionNumber: number;
  updatedAtUtc: string;
};

export type NoteKind = "richtext" | "drawing" | "diagram";

export type NoteDto = {
  id: string;
  locator: number;
  pageId: string;
  noteKind: NoteKind;
  title: string | null;
  contentJsonb: string;
  currentVersionNumber: number;
  sortOrder: number;
  isArchived: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  createdBy: string;
  updatedBy: string;
};

// ── Locator resolution ────────────────────────────────────────────────────

export type LocatorRef = { id: string; locator: number };

export type LocatorAncestors = {
  project: LocatorRef | null;
  cabinet: LocatorRef | null;
  notebook: LocatorRef | null;
  page: LocatorRef | null;
  note: LocatorRef | null;
};

export type LocatorResponse = {
  locator: number;
  kind: "project" | "cabinet" | "notebook" | "page" | "note";
  id: string;
  ancestors: LocatorAncestors;
};

export async function resolveLocator(
  locator: number,
  signal?: AbortSignal
): Promise<LocatorResponse | null> {
  try {
    const { data } = await api.get<LocatorResponse>(`/api/content/locator/${locator}`, {
      signal
    });
    return data;
  } catch (err) {
    // Treat a 404 (stale or guessed locator) as "no resolution" and let the
    // caller fall back to the default landing state.
    if (
      typeof err === "object" &&
      err &&
      "response" in err &&
      (err as { response?: { status?: number } }).response?.status === 404
    ) {
      return null;
    }
    throw err;
  }
}

// ── Projects ───────────────────────────────────────────────────────────────

export type ProjectPageResponse = { items: ProjectDto[]; totalCount: number };

export async function listProjectsPage(
  params: { page?: number; pageSize?: number; search?: string } = {},
  signal?: AbortSignal
): Promise<ProjectPageResponse> {
  const { data } = await api.get<ProjectPageResponse>("/api/content/projects/page", {
    params: {
      page: params.page ?? 0,
      pageSize: params.pageSize ?? 50,
      q: params.search || undefined
    },
    signal
  });
  return data;
}

export async function createProject(name: string, description?: string): Promise<ProjectDto> {
  const { data } = await api.post<ProjectDto>("/api/content/projects", {
    name,
    description
  });
  return data;
}

// ── Cabinets ───────────────────────────────────────────────────────────────

export type CabinetPageResponse = { items: CabinetDto[]; totalCount: number };

export async function listCabinetsPage(
  projectId: string,
  signal?: AbortSignal
): Promise<CabinetPageResponse> {
  const { data } = await api.get<CabinetPageResponse>("/api/content/cabinets/page", {
    params: { projectId, pageSize: 200 },
    signal
  });
  return data;
}

export async function createCabinet(req: {
  projectId: string;
  name: string;
  description?: string;
  icon?: string;
}): Promise<CabinetDto> {
  const { data } = await api.post<CabinetDto>("/api/content/cabinets", req);
  return data;
}

export type UpdateCabinetRequest = {
  name?: string;
  description?: string | null;
  icon?: string | null;
  sortOrder?: number;
  isArchived?: boolean;
  projectId?: string;
};

export async function updateCabinet(
  id: string,
  req: UpdateCabinetRequest
): Promise<CabinetDto> {
  const { data } = await api.patch<CabinetDto>(`/api/content/cabinets/${id}`, req);
  return data;
}

export async function deleteCabinet(id: string): Promise<void> {
  await api.delete(`/api/content/cabinets/${id}`);
}

// ── Notebooks ──────────────────────────────────────────────────────────────

export type NotebookPageResponse = { items: NotebookDto[]; totalCount: number };

export async function listNotebooksPage(
  cabinetId: string,
  signal?: AbortSignal
): Promise<NotebookPageResponse> {
  const { data } = await api.get<NotebookPageResponse>("/api/content/notebooks/page", {
    params: { cabinetId, pageSize: 500 },
    signal
  });
  return data;
}

export async function createNotebook(req: {
  cabinetId: string;
  name: string;
  description?: string;
  icon?: string;
}): Promise<NotebookDto> {
  const { data } = await api.post<NotebookDto>("/api/content/notebooks", req);
  return data;
}

export type UpdateNotebookRequest = {
  name?: string;
  description?: string | null;
  icon?: string | null;
  sortOrder?: number;
  isArchived?: boolean;
  cabinetId?: string;
};

export async function updateNotebook(
  id: string,
  req: UpdateNotebookRequest
): Promise<NotebookDto> {
  const { data } = await api.patch<NotebookDto>(`/api/content/notebooks/${id}`, req);
  return data;
}

export async function deleteNotebook(id: string): Promise<void> {
  await api.delete(`/api/content/notebooks/${id}`);
}

// ── Pages ──────────────────────────────────────────────────────────────────

export async function fetchPageTree(
  notebookId: string,
  signal?: AbortSignal
): Promise<PageTreeNodeDto[]> {
  const { data } = await api.get<PageTreeNodeDto[]>(
    `/api/content/notebooks/${notebookId}/page-tree`,
    { signal }
  );
  return data;
}

export async function fetchPage(id: string, signal?: AbortSignal): Promise<PageDto> {
  const { data } = await api.get<PageDto>(`/api/content/pages/${id}`, { signal });
  return data;
}

export async function createPage(req: {
  notebookId: string;
  parentPageId?: string | null;
  title: string;
  bodyJsonb?: string;
}): Promise<PageDto> {
  const { data } = await api.post<PageDto>("/api/content/pages", req);
  return data;
}

export type UpdatePageRequest = {
  title?: string;
  bodyJsonb?: string;
  sortOrder?: number;
  isArchived?: boolean;
  notebookId?: string;
  parentPageId?: string | null;
  parentPageIdSet?: boolean;
};

export async function updatePage(id: string, req: UpdatePageRequest): Promise<PageDto> {
  const { data } = await api.patch<PageDto>(`/api/content/pages/${id}`, req);
  return data;
}

export async function deletePage(id: string): Promise<void> {
  await api.delete(`/api/content/pages/${id}`);
}

// ── Notes ──────────────────────────────────────────────────────────────────

export async function listNotes(pageId: string, signal?: AbortSignal): Promise<NoteDto[]> {
  const { data } = await api.get<NoteDto[]>(`/api/content/pages/${pageId}/notes`, { signal });
  return data;
}

export async function createNote(
  pageId: string,
  req: { noteKind: NoteKind; title?: string; contentJsonb?: string }
): Promise<NoteDto> {
  const { data } = await api.post<NoteDto>(`/api/content/pages/${pageId}/notes`, req);
  return data;
}

export async function updateNote(
  id: string,
  req: { title?: string; contentJsonb?: string; sortOrder?: number }
): Promise<NoteDto> {
  const { data } = await api.patch<NoteDto>(`/api/content/notes/${id}`, req);
  return data;
}

export async function deleteNote(id: string): Promise<void> {
  await api.delete(`/api/content/notes/${id}`);
}
