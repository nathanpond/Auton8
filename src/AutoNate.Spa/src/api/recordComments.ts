import { api } from "./client";
import { RecordCommentModel, RecordCommentRevisionModel } from "@/types/records";

const base = (recordId: string) => `/api/records/${recordId}/comments`;

export async function listComments(
  recordId: string,
  options: { includeDeleted?: boolean } = {},
  signal?: AbortSignal
): Promise<RecordCommentModel[]> {
  const { data } = await api.get<RecordCommentModel[]>(base(recordId), {
    params: { includeDeleted: options.includeDeleted ?? false },
    signal
  });
  return data;
}

export async function createComment(
  recordId: string,
  body: string
): Promise<RecordCommentModel> {
  const { data } = await api.post<RecordCommentModel>(base(recordId), { body });
  return data;
}

export async function editComment(
  recordId: string,
  commentId: string,
  body: string
): Promise<RecordCommentModel> {
  const { data } = await api.patch<RecordCommentModel>(`${base(recordId)}/${commentId}`, { body });
  return data;
}

export async function deleteComment(recordId: string, commentId: string): Promise<RecordCommentModel> {
  const { data } = await api.delete<RecordCommentModel>(`${base(recordId)}/${commentId}`);
  return data;
}

export async function listCommentRevisions(
  recordId: string,
  commentId: string,
  signal?: AbortSignal
): Promise<RecordCommentRevisionModel[]> {
  const { data } = await api.get<RecordCommentRevisionModel[]>(
    `${base(recordId)}/${commentId}/revisions`,
    { signal }
  );
  return data;
}
