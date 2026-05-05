import { api } from "./client";

export type FormSummary = {
  id: string;
  name: string;
  shortCode: string;
  siteAvailable: boolean;
  isDraft: boolean;
  draftVersionNumber: number;
  publishedVersionNumber: number | null;
  updatedAtUtc: string;
};

export type Form = {
  id: string;
  name: string;
  shortCode: string;
  formCode: string;
  siteAvailable: boolean;
  isDraft: boolean;
  draftVersionNumber: number;
  publishedVersionNumber: number | null;
  createdAtUtc: string;
  createdBy: string;
  updatedAtUtc: string;
  updatedBy: string;
};

export type FormVersionKind = "save" | "publish" | "restore";

export type FormVersion = {
  id: string;
  formId: string;
  versionNumber: number;
  name: string;
  shortCode: string;
  formCode: string;
  siteAvailable: boolean;
  kind: FormVersionKind;
  note: string | null;
  createdAtUtc: string;
  createdBy: string;
};

export type FormDraftSnapshot = {
  id: string;
  name: string;
  shortCode: string;
  formCode: string;
  siteAvailable: boolean;
  draftVersionNumber: number;
  publishedVersionNumber: number | null;
};

export type FormPublishedSnapshot = {
  formId: string;
  name: string;
  shortCode: string;
  formCode: string;
  versionNumber: number;
  publishedAtUtc: string;
};

export type CreateFormRequest = {
  name: string;
  shortCode: string;
  formCode?: string;
  siteAvailable?: boolean;
};

export type SaveFormRequest = {
  name: string;
  shortCode: string;
  formCode: string;
  siteAvailable: boolean;
};

export async function listForms(signal?: AbortSignal): Promise<FormSummary[]> {
  const { data } = await api.get<FormSummary[]>("/api/forms", { signal });
  return data;
}

export async function getForm(id: string, signal?: AbortSignal): Promise<Form | null> {
  try {
    const { data } = await api.get<Form>(`/api/forms/${id}`, { signal });
    return data;
  } catch (error) {
    if (isNotFound(error)) return null;
    throw error;
  }
}

export async function createForm(request: CreateFormRequest): Promise<Form> {
  const { data } = await api.post<Form>("/api/forms", request);
  return data;
}

export async function saveForm(id: string, request: SaveFormRequest): Promise<Form> {
  const { data } = await api.put<Form>(`/api/forms/${id}`, request);
  return data;
}

export async function deleteForm(id: string): Promise<void> {
  await api.delete(`/api/forms/${id}`);
}

export async function publishForm(id: string): Promise<Form> {
  const { data } = await api.post<Form>(`/api/forms/${id}/publish`);
  return data;
}

export async function listFormVersions(id: string, signal?: AbortSignal): Promise<FormVersion[]> {
  const { data } = await api.get<FormVersion[]>(`/api/forms/${id}/versions`, { signal });
  return data;
}

export async function restoreFormVersion(
  id: string,
  versionNumber: number
): Promise<Form> {
  const { data } = await api.post<Form>(`/api/forms/${id}/restore/${versionNumber}`);
  return data;
}

export async function getFormDevSnapshot(
  shortCode: string,
  signal?: AbortSignal
): Promise<FormDraftSnapshot | null> {
  try {
    const { data } = await api.get<FormDraftSnapshot>(
      `/api/forms/dev/${encodeURIComponent(shortCode)}`,
      { signal }
    );
    return data;
  } catch (error) {
    if (isNotFound(error)) return null;
    throw error;
  }
}

export async function getFormPublishedSnapshot(
  shortCode: string,
  signal?: AbortSignal
): Promise<FormPublishedSnapshot | null> {
  try {
    const { data } = await api.get<FormPublishedSnapshot>(
      `/api/forms/public/${encodeURIComponent(shortCode)}`,
      { signal }
    );
    return data;
  } catch (error) {
    if (isNotFound(error)) return null;
    throw error;
  }
}

function isNotFound(error: unknown): boolean {
  const response = (error as { response?: { status?: number } } | undefined)?.response;
  return response?.status === 404;
}
