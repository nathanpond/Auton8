import { api } from "./client";

// DTOs + fetchers for the Documents subsystem.
// Phase 1: folders. Phase 2: documents + document versions.
// Document/template share the same DTO; `kind` discriminates them.

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

// Documents and templates share this DTO; the SPA filters / routes on `kind`.
export type DocumentKind = "document" | "template";

export type DocumentDto = {
  id: string;
  locator: number;
  projectId: string;
  folderId: string | null;
  kind: DocumentKind;
  templateId: string | null;
  title: string;
  description: string | null;
  bodyJsonb: string;
  currentVersionNumber: number;
  sortOrder: number;
  isArchived: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  createdBy: string;
  updatedBy: string;
};

export type DocumentPageResponse = { items: DocumentDto[]; totalCount: number };

// Folder children now returns both arrays so the SPA can render folder
// cards + document rows in one Drive-style grid without two round-trips.
export type FolderChildrenResponse = {
  folders: FolderDto[];
  documents: DocumentDto[];
};

export type DocumentVersionSummaryDto = {
  id: string;
  documentId: string;
  versionNumber: number;
  title: string;
  kind: "autosave" | "manual" | "restore";
  note: string | null;
  createdAtUtc: string;
  createdBy: string;
  createdByName: string | null;
};

export type DocumentVersionDto = DocumentVersionSummaryDto & {
  bodyJsonb: string;
};

export type DocumentVersionsResponse = {
  items: DocumentVersionSummaryDto[];
  totalCount: number;
};

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

// ── Documents ──────────────────────────────────────────────────────────────

// projectId is optional — omitting it returns documents across every
// project the caller can see, gated by Document.View. Used by the
// cross-project templates gallery.
export async function listDocumentsPage(
  params: {
    projectId?: string;
    folderId?: string | null;
    atProjectRoot?: boolean;
    kind?: DocumentKind;
    includeArchived?: boolean;
    page?: number;
    pageSize?: number;
    search?: string;
  },
  signal?: AbortSignal
): Promise<DocumentPageResponse> {
  const { data } = await api.get<DocumentPageResponse>("/api/content/documents/page", {
    params: {
      projectId: params.projectId,
      folderId: params.folderId ?? undefined,
      atProjectRoot: params.atProjectRoot ?? undefined,
      kind: params.kind,
      includeArchived: params.includeArchived ?? undefined,
      page: params.page ?? 0,
      pageSize: params.pageSize ?? 200,
      q: params.search || undefined
    },
    signal
  });
  return data;
}

export async function fetchDocument(
  documentId: string,
  signal?: AbortSignal
): Promise<DocumentDto> {
  const { data } = await api.get<DocumentDto>(`/api/content/documents/${documentId}`, {
    signal
  });
  return data;
}

export async function createDocument(req: {
  projectId: string;
  folderId?: string | null;
  kind?: DocumentKind;
  templateId?: string | null;
  title: string;
  description?: string;
  bodyJsonb?: string;
  sortOrder?: number;
}): Promise<DocumentDto> {
  const { data } = await api.post<DocumentDto>("/api/content/documents", req);
  return data;
}

// `folderIdSet` is the explicit "I want to change folderId" flag — pair it
// with `folderId: null` to move a document to the project root. Mirrors the
// `FolderIdSet` field on the backend UpdateDocumentRequest.
export type UpdateDocumentRequest = {
  projectId?: string;
  folderId?: string | null;
  folderIdSet?: boolean;
  title?: string;
  description?: string | null;
  bodyJsonb?: string;
  sortOrder?: number;
  isArchived?: boolean;
};

export async function updateDocument(
  id: string,
  req: UpdateDocumentRequest
): Promise<DocumentDto> {
  const { data } = await api.patch<DocumentDto>(`/api/content/documents/${id}`, req);
  return data;
}

export async function deleteDocument(id: string): Promise<void> {
  await api.delete(`/api/content/documents/${id}`);
}

// ── Imports (Phase 7) ──────────────────────────────────────────────────────

// Multipart upload of a .docx / .dotx file. The backend dispatches kind
// from the file extension (.docx → 'document', .dotx → 'template'), so
// no `kind` field is sent; the returned DocumentDto's `kind` reflects
// what landed. The editor route should be opened with `?import=1` after
// this resolves so the first mount fetches the stashed buffer and lets
// docx-editor parse it into Yjs.
export async function importDocxAsDocument(req: {
  file: File;
  projectId: string;
  folderId?: string | null;
  title?: string;
}): Promise<DocumentDto> {
  const form = new FormData();
  form.append("file", req.file, req.file.name);
  form.append("projectId", req.projectId);
  if (req.folderId) form.append("folderId", req.folderId);
  if (req.title) form.append("title", req.title);
  const { data } = await api.post<DocumentDto>(
    "/api/content/documents/import",
    form,
    { headers: { "Content-Type": "multipart/form-data" } }
  );
  return data;
}

// Fetch the stashed bytes for the editor's first mount. Returns the raw
// ArrayBuffer docx-editor wants on its `documentBuffer` prop. The fetch
// returns 404 once the editor's first autosave has been followed by the
// matching DELETE — that's the signal that body_jsonb has taken over.
export async function fetchDocumentImportBuffer(
  documentId: string
): Promise<ArrayBuffer> {
  const { data } = await api.get<ArrayBuffer>(
    `/api/content/documents/${documentId}/import-buffer`,
    { responseType: "arraybuffer" }
  );
  return data;
}

export async function discardDocumentImportBuffer(documentId: string): Promise<void> {
  await api.delete(`/api/content/documents/${documentId}/import-buffer`);
}

// ── Templates (Phase 6) ────────────────────────────────────────────────────

// Clone a template into a new document. Server copies the template's
// body_jsonb (rewriting binding placeholders to fresh ids) + clones
// every binding row with no resolved values — the new doc resolves
// fresh on first open or via explicit refresh. Comments are NOT
// cloned (templates are generic; comments live with specific docs).
export async function cloneDocumentFromTemplate(
  templateId: string,
  req: {
    projectId: string;
    folderId?: string | null;
    title: string;
    description?: string;
  }
): Promise<DocumentDto> {
  const { data } = await api.post<DocumentDto>(
    `/api/content/documents/from-template/${templateId}`,
    req
  );
  return data;
}

// ── Document versions ──────────────────────────────────────────────────────

export async function listDocumentVersions(
  documentId: string,
  signal?: AbortSignal
): Promise<DocumentVersionsResponse> {
  const { data } = await api.get<DocumentVersionsResponse>(
    `/api/content/documents/${documentId}/versions`,
    { params: { pageSize: 200 }, signal }
  );
  return data;
}

export async function fetchDocumentVersion(
  documentId: string,
  versionNumber: number,
  signal?: AbortSignal
): Promise<DocumentVersionDto> {
  const { data } = await api.get<DocumentVersionDto>(
    `/api/content/documents/${documentId}/versions/${versionNumber}`,
    { signal }
  );
  return data;
}

export async function restoreDocumentVersion(
  documentId: string,
  versionNumber: number,
  note?: string
): Promise<void> {
  await api.post(
    `/api/content/documents/${documentId}/versions/${versionNumber}/restore`,
    { note }
  );
}

export async function deleteDocumentVersion(
  documentId: string,
  versionNumber: number
): Promise<void> {
  await api.delete(`/api/content/documents/${documentId}/versions/${versionNumber}`);
}
