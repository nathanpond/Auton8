import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  DocumentCommentDto,
  createDocumentComment,
  deleteDocumentComment,
  listDocumentComments,
  reopenDocumentComment,
  replyToDocumentComment,
  resolveDocumentComment
} from "@/api/documentComments";

// React Query layer for the Phase 4 document-comments REST endpoints.
// One cache entry per (documentId, includeResolved) — the editor sidebar
// usually wants resolved threads collapsed-but-visible, so we default to
// includeResolved=true.

export const documentCommentsKey = (documentId: string | null) =>
  ["documents", "comments", documentId] as const;

export function useDocumentComments(documentId: string | null) {
  return useQuery<DocumentCommentDto[]>({
    queryKey: documentCommentsKey(documentId),
    enabled: documentId != null,
    queryFn: async ({ signal }) => {
      const result = await listDocumentComments(
        documentId!,
        { includeResolved: true },
        signal
      );
      return result.items;
    }
  });
}

export function useCreateDocumentComment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: {
      documentId: string;
      number: number;
      bodyText: string;
    }) =>
      createDocumentComment(vars.documentId, {
        number: vars.number,
        bodyText: vars.bodyText
      }),
    onSuccess: (created, vars) => {
      // Optimistic-style cache update: push the canonical row into the
      // list rather than refetching, so the controlled `comments` prop
      // we feed to docx-editor doesn't flicker. Other tabs still need to
      // refetch to see the new comment — that's covered by the React
      // Query refetchOnWindowFocus default for now; SignalR push is a
      // future polish.
      qc.setQueryData<DocumentCommentDto[] | undefined>(
        documentCommentsKey(vars.documentId),
        (prev) => (prev ? [...prev, created] : [created])
      );
    }
  });
}

export function useReplyToDocumentComment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: {
      documentId: string;
      parentCommentId: string;
      number: number;
      bodyText: string;
    }) =>
      replyToDocumentComment(vars.documentId, vars.parentCommentId, {
        number: vars.number,
        bodyText: vars.bodyText
      }),
    onSuccess: (created, vars) => {
      qc.setQueryData<DocumentCommentDto[] | undefined>(
        documentCommentsKey(vars.documentId),
        (prev) => (prev ? [...prev, created] : [created])
      );
    }
  });
}

export function useResolveDocumentComment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { documentId: string; commentId: string }) =>
      resolveDocumentComment(vars.documentId, vars.commentId),
    onSuccess: (_void, vars) => {
      // Whole-thread resolution — easier to refetch than to walk and
      // patch every reply locally.
      qc.invalidateQueries({ queryKey: documentCommentsKey(vars.documentId) });
    }
  });
}

export function useReopenDocumentComment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { documentId: string; commentId: string }) =>
      reopenDocumentComment(vars.documentId, vars.commentId),
    onSuccess: (_void, vars) => {
      qc.invalidateQueries({ queryKey: documentCommentsKey(vars.documentId) });
    }
  });
}

export function useDeleteDocumentComment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { documentId: string; commentId: string }) =>
      deleteDocumentComment(vars.documentId, vars.commentId),
    onSuccess: (_void, vars) => {
      qc.setQueryData<DocumentCommentDto[] | undefined>(
        documentCommentsKey(vars.documentId),
        (prev) => prev?.filter((c) => c.id !== vars.commentId)
      );
    }
  });
}
