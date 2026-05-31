import { api } from "@/api/client";

export type SavedQuery = {
  id: string;
  name: string;
  description: string | null;
  queryText: string;
  isShared: boolean;
  isOwn: boolean;
  ownerUserId: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type CreateSavedQueryRequest = {
  name: string;
  description: string | null;
  queryText: string;
  isShared: boolean;
};

export type UpdateSavedQueryRequest = {
  name?: string;
  description?: string | null;
  queryText?: string;
  isShared?: boolean;
};

export async function listSavedQueries(signal?: AbortSignal): Promise<SavedQuery[]> {
  const { data } = await api.get<SavedQuery[]>("/api/saved-queries/", { signal });
  return data;
}

export async function createSavedQuery(
  req: CreateSavedQueryRequest,
  signal?: AbortSignal
): Promise<SavedQuery> {
  const { data } = await api.post<SavedQuery>("/api/saved-queries/", req, { signal });
  return data;
}

export async function updateSavedQuery(
  id: string,
  req: UpdateSavedQueryRequest,
  signal?: AbortSignal
): Promise<SavedQuery> {
  const { data } = await api.patch<SavedQuery>(`/api/saved-queries/${id}`, req, { signal });
  return data;
}

export async function deleteSavedQuery(id: string, signal?: AbortSignal): Promise<void> {
  await api.delete(`/api/saved-queries/${id}`, { signal });
}

// Phase 3 share-token surface (docs/plans/2026-05-30-data-stores-implementation.md).
// Token issuance returns RawToken once and never again — every subsequent
// GET surfaces metadata only. The SPA copies ShareUrl to the clipboard
// and discards RawToken after that.

export type SavedQueryShareToken = {
  id: string;
  issuedBy: string;
  issuedAtUtc: string;
  expiresAtUtc: string | null;
  revokedAtUtc: string | null;
  maxUses: number | null;
  useCount: number;
  lastUsedAtUtc: string | null;
  label: string | null;
};

export type IssueShareTokenRequest = {
  expiresAtUtc?: string | null;
  maxUses?: number | null;
  label?: string | null;
};

export type IssuedShareToken = {
  token: SavedQueryShareToken;
  rawToken: string;
  shareUrl: string;
};

export async function listSavedQueryShares(
  id: string,
  signal?: AbortSignal
): Promise<SavedQueryShareToken[]> {
  const { data } = await api.get<SavedQueryShareToken[]>(
    `/api/saved-queries/${id}/shares`,
    { signal }
  );
  return data;
}

export async function issueSavedQueryShare(
  id: string,
  request: IssueShareTokenRequest
): Promise<IssuedShareToken> {
  const { data } = await api.post<IssuedShareToken>(
    `/api/saved-queries/${id}/shares`,
    request
  );
  return data;
}

export async function revokeSavedQueryShare(id: string, tokenId: string): Promise<void> {
  await api.delete(`/api/saved-queries/${id}/shares/${tokenId}`);
}
