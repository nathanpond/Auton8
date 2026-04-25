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
