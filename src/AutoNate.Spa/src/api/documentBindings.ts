import { api } from "./client";

// Live data bindings for documents. The document body carries only a
// `{{binding:UUID}}` placeholder; the resolved value lives here and is
// painted over the placeholder by the in-doc decoration plugin.
//
// Snapshot-on-open + explicit refresh — see the plan-mode decision
// (no background polling).

export type DocumentBindingKind = "record-field" | "aql-table";

export type DocumentBindingDto = {
  id: string;
  documentId: string;
  kind: DocumentBindingKind;
  // Stored as the raw JSON string (e.g. `{"recordId":"…","fieldKey":"…"}`).
  // SPA parses with JSON.parse when it needs the typed config.
  configJsonb: string;
  // Last resolved snapshot (also raw JSON string). Null until first
  // refresh — shouldn't happen post-create since the create endpoint
  // resolves immediately, but the schema permits it.
  lastResolvedValueJsonb: string | null;
  lastResolvedAtUtc: string | null;
  lastResolvedByUserId: string | null;
  label: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  createdBy: string;
  updatedBy: string;
};

// Typed shapes the SPA decodes lastResolvedValueJsonb into per kind.
// These mirror what the resolvers emit on the server. Kept in sync by
// convention; if a resolver shape changes, both sides update.

export type RecordFieldResolvedValue = {
  text: string;
  // "text" | "number" | "boolean" | "date" | "json" | "missing" | "denied"
  type: string;
  // The original JSON-typed value (so consumers can right-align numbers
  // or run downstream math). Null when the field was missing or denied.
  rawValue: unknown;
};

export type AqlTableResolvedValue = {
  columns: Array<{ name: string; dataType: string }>;
  rows: Array<Record<string, unknown>>;
  totalCount: number;
  truncated: boolean;
  durationMs: number;
};

export type RecordFieldBindingConfig = {
  recordId: string;
  fieldKey: string;
};

export type AqlTableBindingConfig = {
  queryText: string;
  limit?: number;
};

export type DocumentBindingListResponse = { items: DocumentBindingDto[] };

export type RefreshAllResponse = {
  items: DocumentBindingDto[];
  // Each entry is `{ bindingId: string, error: string }`. Loosely typed
  // because the server emits arbitrary diagnostic shape; tighten if
  // the UI ever needs more.
  failures: Array<{ bindingId?: string; error?: string }>;
};

export async function listDocumentBindings(
  documentId: string,
  signal?: AbortSignal
): Promise<DocumentBindingListResponse> {
  const { data } = await api.get<DocumentBindingListResponse>(
    `/api/content/documents/${documentId}/bindings`,
    { signal }
  );
  return data;
}

export async function createDocumentBinding(
  documentId: string,
  req: { kind: DocumentBindingKind; configJsonb: string; label?: string }
): Promise<DocumentBindingDto> {
  const { data } = await api.post<DocumentBindingDto>(
    `/api/content/documents/${documentId}/bindings`,
    req
  );
  return data;
}

export async function refreshDocumentBinding(
  documentId: string,
  bindingId: string
): Promise<DocumentBindingDto> {
  const { data } = await api.post<DocumentBindingDto>(
    `/api/content/documents/${documentId}/bindings/${bindingId}/refresh`
  );
  return data;
}

export async function refreshAllDocumentBindings(
  documentId: string
): Promise<RefreshAllResponse> {
  const { data } = await api.post<RefreshAllResponse>(
    `/api/content/documents/${documentId}/bindings/refresh-all`
  );
  return data;
}

export async function deleteDocumentBinding(
  documentId: string,
  bindingId: string
): Promise<void> {
  await api.delete(
    `/api/content/documents/${documentId}/bindings/${bindingId}`
  );
}
