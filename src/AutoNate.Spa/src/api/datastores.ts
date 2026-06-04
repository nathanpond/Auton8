import { api } from "./client";

export type DataStoreKind = "FileType" | "SqlType";

export type DataStore = {
  id: string;
  name: string;
  description: string | null;
  kind: number;
  ownerUserId: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  createdBy: string;
  updatedBy: string;
};

export type CreateDataStoreRequest = {
  name: string;
  description?: string | null;
  kind: DataStoreKind;
};

export type UpdateDataStoreRequest = {
  name?: string;
  description?: string | null;
};

export type DataStoreFile = {
  id: string;
  folderPath: string;
  filename: string;
  sizeBytes: number;
  contentType: string | null;
  uploadedAtUtc: string;
};

export type DataStoreFolder = { folderPath: string };

export type DataStoreListing = {
  folders: DataStoreFolder[];
  files: DataStoreFile[];
};

export type CsvColumn = { name: string; postgresType: string };

export type CsvIngestPreview = {
  suggestedTableName: string;
  columns: CsvColumn[];
  sampleRowCount: number;
};

export type CsvIngestResult = {
  tableId: string;
  schemaName: string;
  tableName: string;
  rowsInserted: number;
};

// Returned by GET /api/datastores/{id}/tables — the metadata for every
// ingested table in a SQL DataStore, with the column schema parsed inline
// so the Datasets create modal can do both "pick a source table" and
// "import columns from that table" off a single fetch.
export type DataStoreTable = {
  id: string;
  schemaName: string;
  tableName: string;
  columns: CsvColumn[];
  rowCount: number;
};

const BASE = "/api/datastores";

// Map the persisted smallint into the enum the SPA renders.
export function kindLabel(kind: number): DataStoreKind {
  return kind === 2 ? "SqlType" : "FileType";
}

export async function listDataStores(signal?: AbortSignal): Promise<DataStore[]> {
  const res = await api.get<DataStore[]>(BASE, { signal });
  return res.data;
}

export async function getDataStore(id: string, signal?: AbortSignal): Promise<DataStore> {
  const res = await api.get<DataStore>(`${BASE}/${id}`, { signal });
  return res.data;
}

export async function createDataStore(request: CreateDataStoreRequest): Promise<DataStore> {
  const res = await api.post<DataStore>(BASE, request);
  return res.data;
}

export async function updateDataStore(
  id: string,
  request: UpdateDataStoreRequest
): Promise<DataStore> {
  const res = await api.put<DataStore>(`${BASE}/${id}`, request);
  return res.data;
}

export async function deleteDataStore(id: string): Promise<void> {
  await api.delete(`${BASE}/${id}`);
}

export async function listDataStoreTables(
  id: string,
  signal?: AbortSignal
): Promise<DataStoreTable[]> {
  const res = await api.get<DataStoreTable[]>(`${BASE}/${id}/tables`, { signal });
  return res.data;
}

export async function listDataStoreFiles(
  id: string,
  folder: string,
  signal?: AbortSignal
): Promise<DataStoreListing> {
  const res = await api.get<DataStoreListing>(`${BASE}/${id}/files`, {
    params: { folder },
    signal
  });
  return res.data;
}

export async function uploadDataStoreFile(
  id: string,
  folder: string,
  file: File
): Promise<DataStoreFile> {
  const form = new FormData();
  form.append("folder", folder);
  form.append("file", file);
  const res = await api.post<DataStoreFile>(`${BASE}/${id}/files`, form, {
    headers: { "Content-Type": "multipart/form-data" }
  });
  return res.data;
}

export async function deleteDataStoreFile(id: string, fileId: string): Promise<void> {
  await api.delete(`${BASE}/${id}/files/${fileId}`);
}

// Browsers can issue an authenticated GET to a same-origin URL using the same
// session cookie that backs the API client; returning a plain URL lets the
// consumer use a regular <a href> + download attribute and avoids buffering
// the file through axios (the server sets Content-Disposition).
export function dataStoreFileDownloadUrl(id: string, fileId: string): string {
  return `${BASE}/${id}/files/${fileId}`;
}

export async function createDataStoreFolder(id: string, folderPath: string): Promise<void> {
  await api.post(`${BASE}/${id}/folders`, { folderPath });
}

export async function deleteDataStoreFolder(id: string, folderPath: string): Promise<void> {
  await api.delete(`${BASE}/${id}/folders`, { params: { path: folderPath } });
}

export async function previewCsvIngest(id: string, file: File): Promise<CsvIngestPreview> {
  const form = new FormData();
  form.append("file", file);
  const res = await api.post<CsvIngestPreview>(`${BASE}/${id}/tables/preview`, form, {
    headers: { "Content-Type": "multipart/form-data" }
  });
  return res.data;
}

export async function ingestCsv(
  id: string,
  tableName: string,
  columns: CsvColumn[],
  file: File
): Promise<CsvIngestResult> {
  const form = new FormData();
  form.append("tableName", tableName);
  form.append("columns", JSON.stringify(columns));
  form.append("file", file);
  const res = await api.post<CsvIngestResult>(`${BASE}/${id}/tables`, form, {
    headers: { "Content-Type": "multipart/form-data" }
  });
  return res.data;
}
