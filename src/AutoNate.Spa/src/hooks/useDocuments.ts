import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  FolderDto,
  UpdateFolderRequest,
  createFolder,
  deleteFolder,
  fetchFolder,
  fetchFolderChildren,
  listFoldersPage,
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

// Direct children of a folder. Used by the folder tree when an ancestor is
// expanded so we never materialise the entire project's folder graph upfront.
export function useFolderChildren(folderId: string | null) {
  return useQuery<FolderDto[]>({
    queryKey: folderChildrenKey(folderId),
    enabled: folderId != null,
    queryFn: async ({ signal }) => {
      const result = await fetchFolderChildren(folderId!, signal);
      return result.folders;
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
