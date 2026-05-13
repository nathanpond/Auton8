import { api } from "./client";
import {
  CreateRecordRequest,
  RecordHistoryEntry,
  RecordModel,
  RecordPage,
  SearchRecordsRequest,
  UpdateRecordRequest
} from "@/types/records";

const BASE = "/api/records";

export type ListRecordsParams = {
  recordTypeId: string;
  page?: number;
  pageSize?: number;
  includeArchived?: boolean;
  assigneeId?: string | null;
  sort?: string;
};

export async function listRecords(
  params: ListRecordsParams,
  signal?: AbortSignal
): Promise<RecordPage> {
  const { data } = await api.get<RecordPage>(BASE, {
    params: {
      recordTypeId: params.recordTypeId,
      page: params.page ?? 0,
      pageSize: params.pageSize ?? 25,
      includeArchived: params.includeArchived ?? false,
      assigneeId: params.assigneeId ?? undefined,
      sort: params.sort
    },
    signal
  });
  return data;
}

export type ListAssignedToMeParams = {
  page?: number;
  pageSize?: number;
  includeArchived?: boolean;
  sort?: string;
};

export async function listAssignedToMe(
  params: ListAssignedToMeParams = {},
  signal?: AbortSignal
): Promise<RecordPage> {
  const { data } = await api.get<RecordPage>(`${BASE}/assigned-to-me`, {
    params: {
      page: params.page ?? 0,
      pageSize: params.pageSize ?? 25,
      includeArchived: params.includeArchived ?? false,
      sort: params.sort
    },
    signal
  });
  return data;
}

export async function searchRecords(
  request: SearchRecordsRequest,
  signal?: AbortSignal
): Promise<RecordPage> {
  const { data } = await api.post<RecordPage>(`${BASE}/search`, request, { signal });
  return data;
}

export async function getRecord(id: string, signal?: AbortSignal): Promise<RecordModel | null> {
  try {
    const { data } = await api.get<RecordModel>(`${BASE}/${id}`, { signal });
    return data;
  } catch (error) {
    if (isNotFound(error)) return null;
    throw error;
  }
}

export async function getRecordByKey(
  key: string,
  signal?: AbortSignal
): Promise<RecordModel | null> {
  try {
    const { data } = await api.get<RecordModel>(`${BASE}/by-key/${encodeURIComponent(key)}`, {
      signal
    });
    return data;
  } catch (error) {
    if (isNotFound(error)) return null;
    throw error;
  }
}

export async function createRecord(request: CreateRecordRequest): Promise<RecordModel> {
  const { data } = await api.post<RecordModel>(BASE, request);
  return data;
}

export async function updateRecord(
  id: string,
  request: UpdateRecordRequest
): Promise<RecordModel> {
  const { data } = await api.patch<RecordModel>(`${BASE}/${id}`, request);
  return data;
}

export async function archiveRecord(id: string): Promise<RecordModel> {
  const { data } = await api.delete<RecordModel>(`${BASE}/${id}`);
  return data;
}

export async function restoreRecord(id: string): Promise<RecordModel> {
  const { data } = await api.post<RecordModel>(`${BASE}/${id}/restore`);
  return data;
}

// Hard-delete (vs `archiveRecord` which is the soft tombstone). Server cascades
// clean up edges, comments, history, and watches.
export async function deleteRecord(id: string): Promise<RecordModel> {
  const { data } = await api.delete<RecordModel>(`${BASE}/${id}/permanent`);
  return data;
}

export async function listRecordHistory(
  recordId: string,
  options: { fieldKey?: string; take?: number } = {},
  signal?: AbortSignal
): Promise<RecordHistoryEntry[]> {
  const { data } = await api.get<RecordHistoryEntry[]>(`${BASE}/${recordId}/history`, {
    params: {
      fieldKey: options.fieldKey,
      take: options.take ?? 100
    },
    signal
  });
  return data;
}

function isNotFound(error: unknown): boolean {
  const response = (error as { response?: { status?: number } } | undefined)?.response;
  return response?.status === 404;
}

export type WatchedRecord = {
  id: string;
  recordTypeId: string;
  key: string;
  name: string;
  status: string | null;
  dueDate: string | null;
  description: string | null;
  assigneeIds: string[];
  isArchived: boolean;
  watchedAtUtc: string;
  updatedAtUtc: string;
};

export type WatchedRecordsPage = {
  items: WatchedRecord[];
  totalCount: number;
  page: number;
  pageSize: number;
};

export type ListWatchedParams = {
  page?: number;
  pageSize?: number;
};

export async function listWatchedRecords(
  params: ListWatchedParams = {},
  signal?: AbortSignal
): Promise<WatchedRecordsPage> {
  const { data } = await api.get<WatchedRecordsPage>(`${BASE}/watched-by-me`, {
    params: {
      page: params.page ?? 0,
      pageSize: params.pageSize ?? 25
    },
    signal
  });
  return data;
}

export async function getWatchStatus(
  recordId: string,
  signal?: AbortSignal
): Promise<boolean> {
  const { data } = await api.get<{ isWatching: boolean }>(`${BASE}/${recordId}/watch`, {
    signal
  });
  return data.isWatching;
}

export async function watchRecord(recordId: string): Promise<boolean> {
  const { data } = await api.post<{ isWatching: boolean }>(`${BASE}/${recordId}/watch`);
  return data.isWatching;
}

export async function unwatchRecord(recordId: string): Promise<boolean> {
  const { data } = await api.delete<{ isWatching: boolean }>(`${BASE}/${recordId}/watch`);
  return data.isWatching;
}
