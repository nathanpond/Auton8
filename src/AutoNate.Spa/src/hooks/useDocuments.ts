import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  DocumentDto,
  DocumentKind,
  DocumentVersionsResponse,
  FolderChildrenResponse,
  FolderDto,
  UpdateDocumentRequest,
  UpdateFolderRequest,
  createDocument,
  createFolder,
  deleteDocument,
  deleteDocumentVersion,
  deleteFolder,
  fetchDocument,
  fetchFolder,
  fetchFolderChildren,
  listDocumentVersions,
  listDocumentsPage,
  listFoldersPage,
  restoreDocumentVersion,
  updateDocument,
  updateFolder
} from "@/api/documents";

// React Query hooks for the Documents subsystem. Phase 1 = folders only.
// Query-key convention matches `useContent.ts`: array-shaped, namespaced under
// "documents" so notes-side invalidations don't accidentally bust folder caches
// (and vice versa).

export const projectRootFoldersKey = (projectId: string | null) =>
  ["documents", "folders", "root", projectId] as const;
export const folderChildrenKey = (folderId: string | null) =>
  ["documents", "folders", "children", folderId] as const;
export const folderKey = (folderId: string | null) =>
  ["documents", "folder", folderId] as const;

// All top-level folders in a project (parent_folder_id IS NULL). Sized to
// 200 for the initial fetch — folder trees larger than that should lazy-load
// per parent via useFolderChildren instead.
export function useProjectRootFolders(projectId: string | null) {
  return useQuery<FolderDto[]>({
    queryKey: projectRootFoldersKey(projectId),
    enabled: projectId != null,
    queryFn: async ({ signal }) => {
      const result = await listFoldersPage(
        { projectId: projectId!, atProjectRoot: true, pageSize: 200 },
        signal
      );
      return result.items;
    }
  });
}

// Direct children of a folder — sub-folders + contained documents in one
// envelope. Used by the folder tree (which only reads the .folders array)
// AND by the Drive-style folder view (which renders both arrays).
export function useFolderChildren(folderId: string | null) {
  return useQuery<FolderChildrenResponse>({
    queryKey: folderChildrenKey(folderId),
    enabled: folderId != null,
    queryFn: async ({ signal }) => {
      const result = await fetchFolderChildren(folderId!, signal);
      return result;
    }
  });
}

export function useFolder(folderId: string | null) {
  return useQuery<FolderDto | null>({
    queryKey: folderKey(folderId),
    enabled: folderId != null,
    queryFn: ({ signal }) => fetchFolder(folderId!, signal)
  });
}

export function useCreateFolder() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: createFolder,
    onSuccess: (created) => {
      if (created.parentFolderId) {
        qc.invalidateQueries({ queryKey: folderChildrenKey(created.parentFolderId) });
      } else {
        qc.invalidateQueries({ queryKey: projectRootFoldersKey(created.projectId) });
      }
    }
  });
}

export function useUpdateFolder() {
  const qc = useQueryClient();
  return useMutation({
    // Pass the prior parent + project so we can invalidate the lists the
    // folder was *removed* from on a move. Without this, a folder dragged
    // from /Reports → /Archive shows in both lists until the page refreshes.
    mutationFn: (vars: {
      id: string;
      previousProjectId: string;
      previousParentFolderId: string | null;
      patch: UpdateFolderRequest;
    }) => updateFolder(vars.id, vars.patch),
    onSuccess: (updated, vars) => {
      qc.invalidateQueries({ queryKey: folderKey(updated.id) });
      // Invalidate the new parent's children list.
      if (updated.parentFolderId) {
        qc.invalidateQueries({ queryKey: folderChildrenKey(updated.parentFolderId) });
      } else {
        qc.invalidateQueries({ queryKey: projectRootFoldersKey(updated.projectId) });
      }
      // Invalidate the previous parent's list if the folder was moved.
      const movedParent = vars.previousParentFolderId !== updated.parentFolderId;
      const movedProject = vars.previousProjectId !== updated.projectId;
      if (movedParent || movedProject) {
        if (vars.previousParentFolderId) {
          qc.invalidateQueries({
            queryKey: folderChildrenKey(vars.previousParentFolderId)
          });
        } else {
          qc.invalidateQueries({
            queryKey: projectRootFoldersKey(vars.previousProjectId)
          });
        }
      }
    }
  });
}

export function useDeleteFolder() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { id: string; projectId: string; parentFolderId: string | null }) =>
      deleteFolder(vars.id),
    onSuccess: (_void, vars) => {
      if (vars.parentFolderId) {
        qc.invalidateQueries({ queryKey: folderChildrenKey(vars.parentFolderId) });
      } else {
        qc.invalidateQueries({ queryKey: projectRootFoldersKey(vars.projectId) });
      }
      qc.removeQueries({ queryKey: folderKey(vars.id) });
    }
  });
}

// ── Documents ──────────────────────────────────────────────────────────────

export const projectRootDocumentsKey = (projectId: string | null, kind: DocumentKind) =>
  ["documents", "documents", "root", projectId, kind] as const;
export const documentKey = (documentId: string | null) =>
  ["documents", "document", documentId] as const;
export const documentVersionsKey = (documentId: string | null) =>
  ["documents", "document-versions", documentId] as const;

// All top-level documents in a project (folder_id IS NULL). The kind filter
// keeps document and template lists separate so the template gallery doesn't
// surface live documents (and vice versa).
export function useProjectRootDocuments(
  projectId: string | null,
  kind: DocumentKind = "document"
) {
  return useQuery<DocumentDto[]>({
    queryKey: projectRootDocumentsKey(projectId, kind),
    enabled: projectId != null,
    queryFn: async ({ signal }) => {
      const result = await listDocumentsPage(
        { projectId: projectId!, atProjectRoot: true, kind, pageSize: 200 },
        signal
      );
      return result.items;
    }
  });
}

export function useDocument(documentId: string | null) {
  return useQuery<DocumentDto | null>({
    queryKey: documentKey(documentId),
    enabled: documentId != null,
    queryFn: ({ signal }) => fetchDocument(documentId!, signal)
  });
}

export function useDocumentVersions(documentId: string | null) {
  return useQuery<DocumentVersionsResponse>({
    queryKey: documentVersionsKey(documentId),
    enabled: documentId != null,
    queryFn: ({ signal }) => listDocumentVersions(documentId!, signal)
  });
}

export function useCreateDocument() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: createDocument,
    onSuccess: (created) => {
      if (created.folderId) {
        qc.invalidateQueries({ queryKey: folderChildrenKey(created.folderId) });
      } else {
        qc.invalidateQueries({
          queryKey: projectRootDocumentsKey(created.projectId, created.kind)
        });
      }
    }
  });
}

export function useUpdateDocument() {
  const qc = useQueryClient();
  return useMutation({
    // Pass prior folder/project so a move-induced PATCH invalidates the
    // location the document was *removed* from too.
    mutationFn: (vars: {
      id: string;
      previousProjectId: string;
      previousFolderId: string | null;
      patch: UpdateDocumentRequest;
    }) => updateDocument(vars.id, vars.patch),
    onSuccess: (updated, vars) => {
      qc.invalidateQueries({ queryKey: documentKey(updated.id) });
      qc.invalidateQueries({ queryKey: documentVersionsKey(updated.id) });
      if (updated.folderId) {
        qc.invalidateQueries({ queryKey: folderChildrenKey(updated.folderId) });
      } else {
        qc.invalidateQueries({
          queryKey: projectRootDocumentsKey(updated.projectId, updated.kind)
        });
      }
      const movedFolder = vars.previousFolderId !== updated.folderId;
      const movedProject = vars.previousProjectId !== updated.projectId;
      if (movedFolder || movedProject) {
        if (vars.previousFolderId) {
          qc.invalidateQueries({ queryKey: folderChildrenKey(vars.previousFolderId) });
        } else {
          qc.invalidateQueries({
            queryKey: projectRootDocumentsKey(vars.previousProjectId, updated.kind)
          });
        }
      }
    }
  });
}

export function useDeleteDocument() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: {
      id: string;
      projectId: string;
      folderId: string | null;
      kind: DocumentKind;
    }) => deleteDocument(vars.id),
    onSuccess: (_void, vars) => {
      if (vars.folderId) {
        qc.invalidateQueries({ queryKey: folderChildrenKey(vars.folderId) });
      } else {
        qc.invalidateQueries({
          queryKey: projectRootDocumentsKey(vars.projectId, vars.kind)
        });
      }
      qc.removeQueries({ queryKey: documentKey(vars.id) });
      qc.removeQueries({ queryKey: documentVersionsKey(vars.id) });
    }
  });
}

export function useRestoreDocumentVersion() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { documentId: string; versionNumber: number; note?: string }) =>
      restoreDocumentVersion(vars.documentId, vars.versionNumber, vars.note),
    onSuccess: (_void, vars) => {
      qc.invalidateQueries({ queryKey: documentKey(vars.documentId) });
      qc.invalidateQueries({ queryKey: documentVersionsKey(vars.documentId) });
    }
  });
}

export function useDeleteDocumentVersion() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { documentId: string; versionNumber: number }) =>
      deleteDocumentVersion(vars.documentId, vars.versionNumber),
    onSuccess: (_void, vars) => {
      qc.invalidateQueries({ queryKey: documentVersionsKey(vars.documentId) });
    }
  });
}
