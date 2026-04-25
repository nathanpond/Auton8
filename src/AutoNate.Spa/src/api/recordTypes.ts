import { api } from "./client";
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

const BASE = "/api/record-types";

export async function listRecordTypes(
  includeArchived = false,
  signal?: AbortSignal
): Promise<RecordType[]> {
  const { data } = await api.get<RecordType[]>(BASE, {
    params: { includeArchived },
    signal
  });
  return data;
}

export async function getRecordType(id: string, signal?: AbortSignal): Promise<RecordType | null> {
  try {
    const { data } = await api.get<RecordType>(`${BASE}/${id}`, { signal });
    return data;
  } catch (error) {
    if (isNotFound(error)) return null;
    throw error;
  }
}

export async function createRecordType(request: CreateRecordTypeRequest): Promise<RecordType> {
  const { data } = await api.post<RecordType>(BASE, request);
  return data;
}

export async function updateRecordType(
  id: string,
  request: UpdateRecordTypeRequest
): Promise<RecordType> {
  const { data } = await api.patch<RecordType>(`${BASE}/${id}`, request);
  return data;
}

export async function archiveRecordType(id: string): Promise<RecordType> {
  const { data } = await api.delete<RecordType>(`${BASE}/${id}`);
  return data;
}

export async function restoreRecordType(id: string): Promise<RecordType> {
  const { data } = await api.post<RecordType>(`${BASE}/${id}/restore`);
  return data;
}

export async function listFieldTypes(signal?: AbortSignal): Promise<FieldTypeMetadata[]> {
  const { data } = await api.get<FieldTypeMetadata[]>(`${BASE}/field-types`, { signal });
  return data;
}

export async function listFields(
  recordTypeId: string,
  includeArchived = false,
  signal?: AbortSignal
): Promise<RecordTypeField[]> {
  const { data } = await api.get<RecordTypeField[]>(`${BASE}/${recordTypeId}/fields`, {
    params: { includeArchived },
    signal
  });
  return data;
}

export async function createField(
  recordTypeId: string,
  request: CreateFieldRequest
): Promise<RecordTypeField> {
  const { data } = await api.post<RecordTypeField>(`${BASE}/${recordTypeId}/fields`, request);
  return data;
}

export async function updateField(
  recordTypeId: string,
  fieldId: string,
  request: UpdateFieldRequest
): Promise<RecordTypeField> {
  const { data } = await api.patch<RecordTypeField>(
    `${BASE}/${recordTypeId}/fields/${fieldId}`,
    request
  );
  return data;
}

export async function archiveField(
  recordTypeId: string,
  fieldId: string
): Promise<RecordTypeField> {
  const { data } = await api.delete<RecordTypeField>(`${BASE}/${recordTypeId}/fields/${fieldId}`);
  return data;
}

export async function restoreField(
  recordTypeId: string,
  fieldId: string
): Promise<RecordTypeField> {
  const { data } = await api.post<RecordTypeField>(
    `${BASE}/${recordTypeId}/fields/${fieldId}/restore`
  );
  return data;
}

export async function listRecordTypeAudit(
  recordTypeId: string,
  take = 100,
  signal?: AbortSignal
): Promise<RecordTypeAuditEntry[]> {
  const { data } = await api.get<RecordTypeAuditEntry[]>(`${BASE}/${recordTypeId}/audit`, {
    params: { take },
    signal
  });
  return data;
}

function isNotFound(error: unknown): boolean {
  const response = (error as { response?: { status?: number } } | undefined)?.response;
  return response?.status === 404;
}
