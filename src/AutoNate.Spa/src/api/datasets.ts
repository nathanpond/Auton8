import { api } from "./client";

export type DatasetMode = "Virtual" | "Cached";

export type DatasetColumn = {
  name: string;
  postgresType: string;
};

export type Dataset = {
  id: string;
  name: string;
  description: string | null;
  mode: number; // 1=Virtual, 2=Cached
  columnSchemaJson: string;
  refreshCron: string | null;
  lastRefreshedAtUtc: string | null;
  sourceKind: string; // "datastore" | "dataconnector"
  sourceId: string;
  sourceTableName: string | null;
  // Files-datastore scope. Null for SQL / connector sources.
  fileScopeKind: "file" | "folder" | null;
  fileScopePath: string | null;
  parserKind: "csv" | "raw" | null;
  parserOptionsJson: string | null;
  ownerUserId: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  createdBy: string;
  updatedBy: string;
};

export type CreateDatasetRequest = {
  name: string;
  description?: string | null;
  mode: DatasetMode;
  columns: DatasetColumn[];
  sourceKind: string;
  sourceId: string;
  sourceTableName?: string | null;
  refreshCron?: string | null;
  // Files-datastore scope. Required when sourceKind="datastore" and the
  // datastore is FileType; null otherwise.
  fileScopeKind?: "file" | "folder" | null;
  fileScopePath?: string | null;
  parserKind?: "csv" | "raw" | null;
  parserOptionsJson?: string | null;
};

export type PreviewFileSourceRequest = {
  dataStoreId: string;
  scopeKind: "file" | "folder";
  scopePath: string;
  parserKind: "csv" | "raw";
  parserOptions?: Record<string, string>;
};

export type PreviewFileSourceResponse = {
  columns: DatasetColumn[];
};

export type UpdateDatasetRequest = {
  name?: string;
  description?: string | null;
  refreshCron?: string | null;
};

const BASE = "/api/datasets";

export function modeLabel(mode: number): DatasetMode {
  return mode === 2 ? "Cached" : "Virtual";
}

export async function listDatasets(signal?: AbortSignal): Promise<Dataset[]> {
  const { data } = await api.get<Dataset[]>(BASE, { signal });
  return data;
}

export async function getDataset(id: string, signal?: AbortSignal): Promise<Dataset> {
  const { data } = await api.get<Dataset>(`${BASE}/${id}`, { signal });
  return data;
}

export async function createDataset(request: CreateDatasetRequest): Promise<Dataset> {
  const { data } = await api.post<Dataset>(BASE, request);
  return data;
}

export async function updateDataset(id: string, request: UpdateDatasetRequest): Promise<Dataset> {
  const { data } = await api.put<Dataset>(`${BASE}/${id}`, request);
  return data;
}

export async function deleteDataset(id: string): Promise<void> {
  await api.delete(`${BASE}/${id}`);
}

export async function refreshDataset(id: string): Promise<void> {
  await api.post(`${BASE}/${id}/refresh`);
}

export async function previewFileSource(
  request: PreviewFileSourceRequest,
  signal?: AbortSignal
): Promise<PreviewFileSourceResponse> {
  const { data } = await api.post<PreviewFileSourceResponse>(
    `${BASE}/preview-file-source`,
    request,
    { signal }
  );
  return data;
}
