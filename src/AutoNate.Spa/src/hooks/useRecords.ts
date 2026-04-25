import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  archiveRecord,
  createRecord,
  getRecord,
  getRecordByKey,
  listAssignedToMe,
  ListAssignedToMeParams,
  listRecordHistory,
  ListRecordsParams,
  listRecords,
  restoreRecord,
  searchRecords,
  updateRecord
} from "@/api/records";
import {
  CreateRecordRequest,
  RecordHistoryEntry,
  RecordModel,
  RecordPage,
  SearchRecordsRequest,
  UpdateRecordRequest
} from "@/types/records";

export const recordsListKey = (params: ListRecordsParams) =>
  ["records", "list", params] as const;
export const recordsAssignedToMeKey = (params: ListAssignedToMeParams) =>
  ["records", "assigned-to-me", params] as const;
export const recordsSearchKey = (request: SearchRecordsRequest) =>
  ["records", "search", request] as const;
export const recordKey = (id: string) => ["records", "detail", id] as const;
export const recordByKeyKey = (key: string) => ["records", "by-key", key] as const;
export const recordHistoryKey = (id: string, fieldKey?: string) =>
  ["records", "history", id, { fieldKey: fieldKey ?? null }] as const;

export function useRecords(params: ListRecordsParams, enabled = true) {
  return useQuery<RecordPage>({
    queryKey: recordsListKey(params),
    queryFn: ({ signal }) => listRecords(params, signal),
    enabled: enabled && Boolean(params.recordTypeId)
  });
}

export function useMyAssignedRecords(params: ListAssignedToMeParams = {}, enabled = true) {
  return useQuery<RecordPage>({
    queryKey: recordsAssignedToMeKey(params),
    queryFn: ({ signal }) => listAssignedToMe(params, signal),
    enabled
  });
}

export function useRecordSearch(request: SearchRecordsRequest, enabled = true) {
  return useQuery<RecordPage>({
    queryKey: recordsSearchKey(request),
    queryFn: ({ signal }) => searchRecords(request, signal),
    enabled: enabled && Boolean(request.recordTypeId)
  });
}

export function useRecord(id: string | null) {
  return useQuery<RecordModel | null>({
    queryKey: recordKey(id ?? "unset"),
    queryFn: ({ signal }) => (id ? getRecord(id, signal) : Promise.resolve(null)),
    enabled: Boolean(id)
  });
}

export function useRecordByKey(key: string | null) {
  return useQuery<RecordModel | null>({
    queryKey: recordByKeyKey(key ?? "unset"),
    queryFn: ({ signal }) => (key ? getRecordByKey(key, signal) : Promise.resolve(null)),
    enabled: Boolean(key)
  });
}

export function useRecordHistory(recordId: string | null, fieldKey?: string) {
  return useQuery<RecordHistoryEntry[]>({
    queryKey: recordHistoryKey(recordId ?? "unset", fieldKey),
    queryFn: ({ signal }) =>
      recordId ? listRecordHistory(recordId, { fieldKey }, signal) : Promise.resolve([]),
    enabled: Boolean(recordId)
  });
}

export function useCreateRecord() {
  const qc = useQueryClient();
  return useMutation<RecordModel, Error, CreateRecordRequest>({
    mutationFn: createRecord,
    onSuccess: (created) => {
      qc.invalidateQueries({ queryKey: ["records"] });
      qc.setQueryData(recordKey(created.id), created);
    }
  });
}

export function useUpdateRecord(id: string) {
  const qc = useQueryClient();
  return useMutation<RecordModel, Error, UpdateRecordRequest>({
    mutationFn: (request) => updateRecord(id, request),
    onSuccess: (updated) => {
      qc.invalidateQueries({ queryKey: ["records"] });
      qc.setQueryData(recordKey(updated.id), updated);
    }
  });
}

export function useArchiveRecord() {
  const qc = useQueryClient();
  return useMutation<RecordModel, Error, string>({
    mutationFn: archiveRecord,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["records"] })
  });
}

export function useRestoreRecord() {
  const qc = useQueryClient();
  return useMutation<RecordModel, Error, string>({
    mutationFn: restoreRecord,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["records"] })
  });
}
