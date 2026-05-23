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
