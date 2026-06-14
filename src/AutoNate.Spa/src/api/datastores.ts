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

export type CsvIngestMode = "insert" | "append" | "replace";

export type CsvIngestResult = {
  tableId: string;
  schemaName: string;
  tableName: string;
  rowsInserted: number;
  // Mode-outcome metadata. Replaced/Appended are mutually exclusive — both
  // false on a fresh insert. PreviousRowCount is the row count of the
  // existing table before this call (null on fresh insert). SchemaChanged
  // is only meaningful on the replace path.
  replaced: boolean;
  appended: boolean;
  previousRowCount: number | null;
  schemaChanged: boolean;
};

// Server returns this 409 body when POST /tables hits an existing table.
// conflictKind:
//   - "exists": caller passed mode=insert (or omitted). Operator can pick
//     append or replace from the conflict UI.
//   - "schemaMismatch": caller passed mode=append but the schemas differ.
//     The UI keeps the operator in the conflict view with Append disabled
//     and the diff displayed; the only paths forward are Replace or Cancel.
export type CsvIngestConflictKind = "exists" | "schemaMismatch";

export type CsvIngestConflict = {
  reason: string;
  conflictKind: CsvIngestConflictKind;
  existingTableId: string;
  sanitizedTableName: string;
  existingRowCount: number;
  existingColumns: CsvColumn[];
  // Only set when conflictKind === "schemaMismatch".
  incomingColumns?: CsvColumn[];
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

export type DataStoreTablePreviewColumn = { name: string; postgresType: string };

// Top-N preview of the rows physically stored in `ds_<id>.<table>`. Lives
// on the DataStore detail page so an admin can sanity-check an ingest
// without first defining a Dataset over the table. Server hard-caps the
// row count (currently 200) regardless of what limit we pass.
export type DataStoreTablePreview = {
  schemaName: string;
  tableName: string;
  columns: DataStoreTablePreviewColumn[];
  rows: Record<string, unknown>[];
  totalRowCount: number;
};

export async function previewDataStoreTable(
  id: string,
  tableId: string,
  limit?: number,
  signal?: AbortSignal
): Promise<DataStoreTablePreview> {
  const res = await api.get<DataStoreTablePreview>(
    `${BASE}/${id}/tables/${tableId}/preview`,
    { params: limit ? { limit } : undefined, signal }
  );
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

// In-memory download for code paths that need the bytes (not a browser
// download) — e.g. piping a file in one datastore into a CSV-ingest call on
// another. Wraps the response in a `File` so callers can hand it to the
// multipart-form ingest helpers below without an extra Blob→File hop.
export async function downloadDataStoreFileAsFile(
  id: string,
  fileId: string,
  filename: string
): Promise<File> {
  const res = await api.get<Blob>(`${BASE}/${id}/files/${fileId}`, {
    responseType: "blob"
  });
  return new File([res.data], filename, {
    type: res.data.type || "application/octet-stream"
  });
}

export async function createDataStoreFolder(id: string, folderPath: string): Promise<void> {
  await api.post(`${BASE}/${id}/folders`, { folderPath });
}

export async function deleteDataStoreFolder(id: string, folderPath: string): Promise<void> {
  await api.delete(`${BASE}/${id}/folders`, { params: { path: folderPath } });
}

// Rename and/or move a file. Pass null for fields you don't want to change.
export async function renameOrMoveDataStoreFile(
  id: string,
  fileId: string,
  newFolderPath: string | null,
  newFilename: string | null
): Promise<DataStoreFile> {
  const res = await api.patch<DataStoreFile>(`${BASE}/${id}/files/${fileId}`, {
    newFolderPath,
    newFilename
  });
  return res.data;
}

export async function copyDataStoreFile(
  id: string,
  fileId: string,
  targetFolderPath: string,
  newFilename: string | null
): Promise<DataStoreFile> {
  const res = await api.post<DataStoreFile>(`${BASE}/${id}/files/${fileId}/copy`, {
    targetFolderPath,
    newFilename
  });
  return res.data;
}

export async function renameOrMoveDataStoreFolder(
  id: string,
  path: string,
  newPath: string
): Promise<void> {
  await api.patch(`${BASE}/${id}/folders`, { path, newPath });
}

export async function copyDataStoreFolder(
  id: string,
  sourcePath: string,
  targetPath: string
): Promise<void> {
  await api.post(`${BASE}/${id}/folders/copy`, { sourcePath, targetPath });
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
  file: File,
  mode: CsvIngestMode = "insert"
): Promise<CsvIngestResult> {
  const form = new FormData();
  form.append("tableName", tableName);
  form.append("columns", JSON.stringify(columns));
  form.append("file", file);
  if (mode !== "insert") form.append("mode", mode);
  const res = await api.post<CsvIngestResult>(`${BASE}/${id}/tables`, form, {
    headers: { "Content-Type": "multipart/form-data" }
  });
  return res.data;
}

// Type guard for the 409 conflict body returned by POST /tables when a
// table by that name already exists. Callers should catch the axios error,
// inspect response.status === 409, and pass response.data through this to
// drive the SPA's append/replace confirm flow.
export function isCsvIngestConflict(body: unknown): body is CsvIngestConflict {
  if (!body || typeof body !== "object") return false;
  const b = body as Record<string, unknown>;
  return (
    typeof b.existingTableId === "string" &&
    typeof b.sanitizedTableName === "string" &&
    typeof b.existingRowCount === "number" &&
    Array.isArray(b.existingColumns) &&
    (b.conflictKind === "exists" || b.conflictKind === "schemaMismatch")
  );
}
