import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  archiveField,
  archiveRecordType,
  createField,
  createRecordType,
  getRecordType,
  listFieldTypes,
  listFields,
  listRecordTypeAudit,
  listRecordTypes,
  restoreField,
  restoreRecordType,
  updateField,
  updateRecordType
} from "@/api/recordTypes";
import {
  CreateFieldRequest,
  CreateRecordTypeRequest,
  FieldTypeMetadata,
  RecordType,
  RecordTypeAuditEntry,
  RecordTypeField,
  UpdateFieldRequest,
  UpdateRecordTypeRequest
} from "@/types/records";

export const RECORD_TYPES_QUERY_KEY = (includeArchived: boolean) =>
  ["record-types", { includeArchived }] as const;
export const RECORD_TYPE_QUERY_KEY = (id: string) => ["record-types", "detail", id] as const;
export const RECORD_TYPE_FIELDS_QUERY_KEY = (id: string, includeArchived: boolean) =>
  ["record-types", "fields", id, { includeArchived }] as const;
export const RECORD_TYPE_AUDIT_QUERY_KEY = (id: string) => ["record-types", "audit", id] as const;
export const FIELD_TYPES_QUERY_KEY = ["record-types", "field-types"] as const;

export function useRecordTypes(includeArchived = false) {
  return useQuery<RecordType[]>({
    queryKey: RECORD_TYPES_QUERY_KEY(includeArchived),
    queryFn: ({ signal }) => listRecordTypes(includeArchived, signal)
  });
}

export function useRecordType(id: string | null) {
  return useQuery<RecordType | null>({
    queryKey: RECORD_TYPE_QUERY_KEY(id ?? "unset"),
    queryFn: ({ signal }) => (id ? getRecordType(id, signal) : Promise.resolve(null)),
    enabled: Boolean(id)
  });
}

export function useRecordTypeFields(id: string | null, includeArchived = false) {
  return useQuery<RecordTypeField[]>({
    queryKey: RECORD_TYPE_FIELDS_QUERY_KEY(id ?? "unset", includeArchived),
    queryFn: ({ signal }) => (id ? listFields(id, includeArchived, signal) : Promise.resolve([])),
    enabled: Boolean(id)
  });
}

export function useRecordTypeAudit(id: string | null) {
  return useQuery<RecordTypeAuditEntry[]>({
    queryKey: RECORD_TYPE_AUDIT_QUERY_KEY(id ?? "unset"),
    queryFn: ({ signal }) => (id ? listRecordTypeAudit(id, 100, signal) : Promise.resolve([])),
    enabled: Boolean(id)
  });
}

export function useFieldTypes() {
  return useQuery<FieldTypeMetadata[]>({
    queryKey: FIELD_TYPES_QUERY_KEY,
    queryFn: ({ signal }) => listFieldTypes(signal),
    staleTime: 10 * 60 * 1000
  });
}

export function useCreateRecordType() {
  const qc = useQueryClient();
  return useMutation<RecordType, Error, CreateRecordTypeRequest>({
    mutationFn: createRecordType,
    onSuccess: (created) => {
      qc.invalidateQueries({ queryKey: ["record-types"] });
      qc.setQueryData(RECORD_TYPE_QUERY_KEY(created.id), created);
    }
  });
}

export function useUpdateRecordType(id: string) {
  const qc = useQueryClient();
  return useMutation<RecordType, Error, UpdateRecordTypeRequest>({
    mutationFn: (request) => updateRecordType(id, request),
    onSuccess: (updated) => {
      qc.invalidateQueries({ queryKey: ["record-types"] });
      qc.setQueryData(RECORD_TYPE_QUERY_KEY(id), updated);
    }
  });
}

export function useArchiveRecordType() {
  const qc = useQueryClient();
  return useMutation<RecordType, Error, string>({
    mutationFn: archiveRecordType,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["record-types"] });
    }
  });
}

export function useRestoreRecordType() {
  const qc = useQueryClient();
  return useMutation<RecordType, Error, string>({
    mutationFn: restoreRecordType,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["record-types"] });
    }
  });
}

export function useCreateField(recordTypeId: string) {
  const qc = useQueryClient();
  return useMutation<RecordTypeField, Error, CreateFieldRequest>({
    mutationFn: (request) => createField(recordTypeId, request),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["record-types", "fields", recordTypeId] });
      qc.invalidateQueries({ queryKey: RECORD_TYPE_AUDIT_QUERY_KEY(recordTypeId) });
    }
  });
}

export function useUpdateField(recordTypeId: string) {
  const qc = useQueryClient();
  return useMutation<RecordTypeField, Error, { fieldId: string; request: UpdateFieldRequest }>({
    mutationFn: ({ fieldId, request }) => updateField(recordTypeId, fieldId, request),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["record-types", "fields", recordTypeId] });
      qc.invalidateQueries({ queryKey: RECORD_TYPE_AUDIT_QUERY_KEY(recordTypeId) });
    }
  });
}

export function useArchiveField(recordTypeId: string) {
  const qc = useQueryClient();
  return useMutation<RecordTypeField, Error, string>({
    mutationFn: (fieldId) => archiveField(recordTypeId, fieldId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["record-types", "fields", recordTypeId] });
      qc.invalidateQueries({ queryKey: RECORD_TYPE_AUDIT_QUERY_KEY(recordTypeId) });
    }
  });
}

export function useRestoreField(recordTypeId: string) {
  const qc = useQueryClient();
  return useMutation<RecordTypeField, Error, string>({
    mutationFn: (fieldId) => restoreField(recordTypeId, fieldId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["record-types", "fields", recordTypeId] });
      qc.invalidateQueries({ queryKey: RECORD_TYPE_AUDIT_QUERY_KEY(recordTypeId) });
    }
  });
}
