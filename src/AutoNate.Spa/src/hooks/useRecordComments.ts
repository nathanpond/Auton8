import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  createComment,
  deleteComment,
  editComment,
  listComments,
  listCommentRevisions
} from "@/api/recordComments";
import { RecordCommentModel, RecordCommentRevisionModel } from "@/types/records";

export const recordCommentsKey = (recordId: string, includeDeleted: boolean) =>
  ["records", "comments", recordId, { includeDeleted }] as const;
export const recordCommentRevisionsKey = (recordId: string, commentId: string) =>
  ["records", "comments", "revisions", recordId, commentId] as const;

export function useRecordComments(recordId: string | null, includeDeleted = false) {
  return useQuery<RecordCommentModel[]>({
    queryKey: recordCommentsKey(recordId ?? "unset", includeDeleted),
    queryFn: ({ signal }) =>
      recordId ? listComments(recordId, { includeDeleted }, signal) : Promise.resolve([]),
    enabled: Boolean(recordId)
  });
}

export function useCommentRevisions(recordId: string | null, commentId: string | null) {
  return useQuery<RecordCommentRevisionModel[]>({
    queryKey: recordCommentRevisionsKey(recordId ?? "unset", commentId ?? "unset"),
    queryFn: ({ signal }) =>
      recordId && commentId
        ? listCommentRevisions(recordId, commentId, signal)
        : Promise.resolve([]),
    enabled: Boolean(recordId && commentId)
  });
}

export function useCreateComment(recordId: string) {
  const qc = useQueryClient();
  return useMutation<RecordCommentModel, Error, string>({
    mutationFn: (body) => createComment(recordId, body),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["records", "comments", recordId] })
  });
}

export function useEditComment(recordId: string) {
  const qc = useQueryClient();
  return useMutation<RecordCommentModel, Error, { commentId: string; body: string }>({
    mutationFn: ({ commentId, body }) => editComment(recordId, commentId, body),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["records", "comments", recordId] });
      qc.invalidateQueries({
        queryKey: recordCommentRevisionsKey(recordId, vars.commentId)
      });
    }
  });
}

export function useDeleteComment(recordId: string) {
  const qc = useQueryClient();
  return useMutation<RecordCommentModel, Error, string>({
    mutationFn: (commentId) => deleteComment(recordId, commentId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["records", "comments", recordId] })
  });
}
