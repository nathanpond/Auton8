import { api } from "./client";
import {
  CreateEdgeFieldRequest,
  CreateEdgeRequest,
  CreateEdgeTypeRequest,
  Edge,
  EdgeDirection,
  EdgeType,
  EdgeTypeField,
  UpdateEdgeFieldRequest,
  UpdateEdgeTypeRequest
} from "@/types/records";

const TYPES = "/api/record-edge-types";
const EDGES = "/api/record-edges";

export async function listEdgeTypes(includeArchived = false, signal?: AbortSignal): Promise<EdgeType[]> {
  const { data } = await api.get<EdgeType[]>(TYPES, { params: { includeArchived }, signal });
  return data;
}

export async function getEdgeType(id: string, signal?: AbortSignal): Promise<EdgeType | null> {
  try {
    const { data } = await api.get<EdgeType>(`${TYPES}/${id}`, { signal });
    return data;
  } catch (error) {
    if (isNotFound(error)) return null;
    throw error;
  }
}

export async function createEdgeType(request: CreateEdgeTypeRequest): Promise<EdgeType> {
  const { data } = await api.post<EdgeType>(TYPES, request);
  return data;
}

export async function updateEdgeType(id: string, request: UpdateEdgeTypeRequest): Promise<EdgeType> {
  const { data } = await api.patch<EdgeType>(`${TYPES}/${id}`, request);
  return data;
}

export async function archiveEdgeType(id: string): Promise<EdgeType> {
  const { data } = await api.delete<EdgeType>(`${TYPES}/${id}`);
  return data;
}

export async function restoreEdgeType(id: string): Promise<EdgeType> {
  const { data } = await api.post<EdgeType>(`${TYPES}/${id}/restore`);
  return data;
}

export async function listEdgeTypeFields(edgeTypeId: string, signal?: AbortSignal): Promise<EdgeTypeField[]> {
  const { data } = await api.get<EdgeTypeField[]>(`${TYPES}/${edgeTypeId}/fields`, { signal });
  return data;
}

export async function createEdgeTypeField(
  edgeTypeId: string,
  request: CreateEdgeFieldRequest
): Promise<EdgeTypeField> {
  const { data } = await api.post<EdgeTypeField>(`${TYPES}/${edgeTypeId}/fields`, request);
  return data;
}

export async function updateEdgeTypeField(
  edgeTypeId: string,
  fieldId: string,
  request: UpdateEdgeFieldRequest
): Promise<EdgeTypeField> {
  const { data } = await api.patch<EdgeTypeField>(`${TYPES}/${edgeTypeId}/fields/${fieldId}`, request);
  return data;
}

export async function deleteEdgeTypeField(edgeTypeId: string, fieldId: string): Promise<void> {
  await api.delete(`${TYPES}/${edgeTypeId}/fields/${fieldId}`);
}

export async function createEdge(request: CreateEdgeRequest): Promise<Edge> {
  const { data } = await api.post<Edge>(EDGES, request);
  return data;
}

export async function deleteEdge(id: string): Promise<void> {
  await api.delete(`${EDGES}/${id}`);
}

export async function listEdgesForRecord(
  recordId: string,
  options: { direction?: EdgeDirection; edgeTypeId?: string } = {},
  signal?: AbortSignal
): Promise<Edge[]> {
  const { data } = await api.get<Edge[]>(`/api/records/${recordId}/edges`, {
    params: { direction: options.direction, edgeTypeId: options.edgeTypeId },
    signal
  });
  return data;
}

function isNotFound(error: unknown): boolean {
  const response = (error as { response?: { status?: number } } | undefined)?.response;
  return response?.status === 404;
}
