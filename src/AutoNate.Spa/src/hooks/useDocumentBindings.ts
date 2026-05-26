import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  DocumentBindingDto,
  DocumentBindingKind,
  createDocumentBinding,
  deleteDocumentBinding,
  listDocumentBindings,
  refreshAllDocumentBindings,
  refreshDocumentBinding
} from "@/api/documentBindings";

// React Query layer for document bindings.
//
// Snapshot-on-open semantics: bindings are fetched once on document
// open + on explicit refresh actions. No background refetch — we
// disable refetchOnWindowFocus for this query so coming back to the
// tab doesn't silently re-resolve every binding.

export const documentBindingsKey = (documentId: string | null) =>
  ["documents", "bindings", documentId] as const;

export function useDocumentBindings(documentId: string | null) {
  return useQuery<DocumentBindingDto[]>({
    queryKey: documentBindingsKey(documentId),
    enabled: documentId != null,
    refetchOnWindowFocus: false,
    queryFn: async ({ signal }) => {
      const result = await listDocumentBindings(documentId!, signal);
      return result.items;
    }
  });
}

export function useCreateDocumentBinding() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: {
      documentId: string;
      kind: DocumentBindingKind;
      configJsonb: string;
      label?: string;
    }) =>
      createDocumentBinding(vars.documentId, {
        kind: vars.kind,
        configJsonb: vars.configJsonb,
        label: vars.label
      }),
    onSuccess: (created, vars) => {
      // Append to the cached list rather than refetching so the
      // decoration plugin can find the new row immediately by id.
      qc.setQueryData<DocumentBindingDto[] | undefined>(
        documentBindingsKey(vars.documentId),
        (prev) => (prev ? [...prev, created] : [created])
      );
    }
  });
}

export function useRefreshDocumentBinding() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { documentId: string; bindingId: string }) =>
      refreshDocumentBinding(vars.documentId, vars.bindingId),
    onSuccess: (updated, vars) => {
      // Replace the single row in the cached list — keep order stable
      // so the side panel doesn't visually reshuffle after a refresh.
      qc.setQueryData<DocumentBindingDto[] | undefined>(
        documentBindingsKey(vars.documentId),
        (prev) => prev?.map((b) => (b.id === updated.id ? updated : b))
      );
    }
  });
}

export function useRefreshAllDocumentBindings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { documentId: string }) =>
      refreshAllDocumentBindings(vars.documentId),
    onSuccess: (response, vars) => {
      // Server returns the fresh list — replace the cache wholesale.
      qc.setQueryData(documentBindingsKey(vars.documentId), response.items);
    }
  });
}

export function useDeleteDocumentBinding() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { documentId: string; bindingId: string }) =>
      deleteDocumentBinding(vars.documentId, vars.bindingId),
    onSuccess: (_void, vars) => {
      qc.setQueryData<DocumentBindingDto[] | undefined>(
        documentBindingsKey(vars.documentId),
        (prev) => prev?.filter((b) => b.id !== vars.bindingId)
      );
    }
  });
}
