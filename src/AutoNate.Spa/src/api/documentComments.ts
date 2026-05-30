import { api } from "./client";

// Threaded comments for documents. The body's commentRangeStart/End
// markers live in the Y.Doc (synced via Hocuspocus); thread metadata
// (author, body, replies, resolved status) lives in Postgres and travels
// through these endpoints.
//
// `number` is the docx-editor-facing numeric id (matches the OOXML
// `w:comment id="N"` it writes into the body markers). `id` is our
// canonical Guid — used in URLs, audit, and the response body so the SPA
// can resolve a comment to its row regardless of any number renumbering.

export type DocumentCommentDto = {
  id: string;
  documentId: string;
  number: number;
  parentCommentId: string | null;
  threadId: string;
  authorId: string;
  authorName: string | null;
  bodyText: string;
  resolvedAtUtc: string | null;
  resolvedByUserId: string | null;
  resolvedByUserName: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type DocumentCommentListResponse = { items: DocumentCommentDto[] };

export async function listDocumentComments(
  documentId: string,
  options: { includeResolved?: boolean } = {},
  signal?: AbortSignal
): Promise<DocumentCommentListResponse> {
  const { data } = await api.get<DocumentCommentListResponse>(
    `/api/content/documents/${documentId}/comments`,
    {
      params: { includeResolved: options.includeResolved },
      signal
    }
  );
  return data;
}

export async function createDocumentComment(
  documentId: string,
  req: { number: number; bodyText: string }
): Promise<DocumentCommentDto> {
  const { data } = await api.post<DocumentCommentDto>(
    `/api/content/documents/${documentId}/comments`,
    req
  );
  return data;
}

export async function replyToDocumentComment(
  documentId: string,
  parentCommentId: string,
  req: { number: number; bodyText: string }
): Promise<DocumentCommentDto> {
  const { data } = await api.post<DocumentCommentDto>(
    `/api/content/documents/${documentId}/comments/${parentCommentId}/replies`,
    req
  );
  return data;
}

export async function resolveDocumentComment(
  documentId: string,
  commentId: string
): Promise<void> {
  await api.post(
    `/api/content/documents/${documentId}/comments/${commentId}/resolve`
  );
}

export async function reopenDocumentComment(
  documentId: string,
  commentId: string
): Promise<void> {
  await api.post(
    `/api/content/documents/${documentId}/comments/${commentId}/reopen`
  );
}

export async function deleteDocumentComment(
  documentId: string,
  commentId: string
): Promise<void> {
  await api.delete(
    `/api/content/documents/${documentId}/comments/${commentId}`
  );
}
