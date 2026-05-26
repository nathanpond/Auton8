import { api } from "./client";

// DTOs + fetchers for the Documents subsystem (Phase 1 ships folders only —
// document/template/binding/comment/version/export DTOs land in later phases).
// Mirrors the camelCase wire shape of /api/content/folders/* and the
// FolderDto record in ContentFolderEndpoints.cs.

export type FolderDto = {
  id: string;
  locator: number;
  projectId: string;
  parentFolderId: string | null;
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

export type FolderPageResponse = { items: FolderDto[]; totalCount: number };

export type FolderChildrenResponse = { folders: FolderDto[] };

// List folders for a project. `parentFolderId` filters to a specific subtree's
// direct children; `atProjectRoot=true` returns only top-level (parent_folder_id
// IS NULL) folders. Passing neither returns every folder the caller can View
// across the project (used for the lazy-load folder tree when an ancestor is
// expanded all at once).
export async function listFoldersPage(
  params: {
    projectId: string;
    parentFolderId?: string | null;
    atProjectRoot?: boolean;
    page?: number;
    pageSize?: number;
    search?: string;
  },
  signal?: AbortSignal
): Promise<FolderPageResponse> {
  const { data } = await api.get<FolderPageResponse>("/api/content/folders/page", {
    params: {
      projectId: params.projectId,
      parentFolderId: params.parentFolderId ?? undefined,
      atProjectRoot: params.atProjectRoot ?? undefined,
      page: params.page ?? 0,
      pageSize: params.pageSize ?? 200,
      q: params.search || undefined
    },
    signal
  });
  return data;
}

export async function fetchFolderChildren(
  folderId: string,
  signal?: AbortSignal
): Promise<FolderChildrenResponse> {
  const { data } = await api.get<FolderChildrenResponse>(
    `/api/content/folders/${folderId}/children`,
    { signal }
  );
  return data;
}

export async function fetchFolder(
  folderId: string,
  signal?: AbortSignal
): Promise<FolderDto> {
  const { data } = await api.get<FolderDto>(`/api/content/folders/${folderId}`, { signal });
  return data;
}

export async function createFolder(req: {
  projectId: string;
  parentFolderId?: string | null;
  name: string;
  description?: string;
  icon?: string;
  sortOrder?: number;
}): Promise<FolderDto> {
  const { data } = await api.post<FolderDto>("/api/content/folders", req);
  return data;
}

export type UpdateFolderRequest = {
  projectId?: string;
  parentFolderId?: string | null;
  name?: string;
  description?: string | null;
  icon?: string | null;
  sortOrder?: number;
  isArchived?: boolean;
};

export async function updateFolder(
  id: string,
  req: UpdateFolderRequest
): Promise<FolderDto> {
  const { data } = await api.patch<FolderDto>(`/api/content/folders/${id}`, req);
  return data;
}

export async function deleteFolder(id: string): Promise<void> {
  await api.delete(`/api/content/folders/${id}`);
}
