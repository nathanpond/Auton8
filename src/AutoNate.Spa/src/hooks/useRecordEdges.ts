import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  archiveEdgeType,
  createEdge,
  createEdgeType,
  createEdgeTypeField,
  deleteEdge,
  deleteEdgeTypeField,
  getEdgeType,
  listEdgeTypeFields,
  listEdgeTypes,
  listEdgesForRecord,
  restoreEdgeType,
  updateEdgeType,
  updateEdgeTypeField
} from "@/api/recordEdges";
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

export const edgeTypesKey = (includeArchived: boolean) =>
  ["edge-types", { includeArchived }] as const;
export const edgeTypeKey = (id: string) => ["edge-types", "detail", id] as const;
export const edgeTypeFieldsKey = (id: string) => ["edge-types", "fields", id] as const;
export const recordEdgesKey = (recordId: string, direction: EdgeDirection) =>
  ["records", "edges", recordId, direction] as const;

export function useEdgeTypes(includeArchived = false) {
  return useQuery<EdgeType[]>({
    queryKey: edgeTypesKey(includeArchived),
    queryFn: ({ signal }) => listEdgeTypes(includeArchived, signal)
  });
}

export function useEdgeType(id: string | null) {
  return useQuery<EdgeType | null>({
    queryKey: edgeTypeKey(id ?? "unset"),
    queryFn: ({ signal }) => (id ? getEdgeType(id, signal) : Promise.resolve(null)),
    enabled: Boolean(id)
  });
}

export function useEdgeTypeFields(id: string | null) {
  return useQuery<EdgeTypeField[]>({
    queryKey: edgeTypeFieldsKey(id ?? "unset"),
    queryFn: ({ signal }) => (id ? listEdgeTypeFields(id, signal) : Promise.resolve([])),
    enabled: Boolean(id)
  });
}

export function useRecordEdges(recordId: string | null, direction: EdgeDirection = "both") {
  return useQuery<Edge[]>({
    queryKey: recordEdgesKey(recordId ?? "unset", direction),
    queryFn: ({ signal }) =>
      recordId ? listEdgesForRecord(recordId, { direction }, signal) : Promise.resolve([]),
    enabled: Boolean(recordId)
  });
}

export function useCreateEdgeType() {
  const qc = useQueryClient();
  return useMutation<EdgeType, Error, CreateEdgeTypeRequest>({
    mutationFn: createEdgeType,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["edge-types"] })
  });
}

export function useUpdateEdgeType(id: string) {
  const qc = useQueryClient();
  return useMutation<EdgeType, Error, UpdateEdgeTypeRequest>({
    mutationFn: (req) => updateEdgeType(id, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["edge-types"] })
  });
}

export function useArchiveEdgeType() {
  const qc = useQueryClient();
  return useMutation<EdgeType, Error, string>({
    mutationFn: archiveEdgeType,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["edge-types"] })
  });
}

export function useRestoreEdgeType() {
  const qc = useQueryClient();
  return useMutation<EdgeType, Error, string>({
    mutationFn: restoreEdgeType,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["edge-types"] })
  });
}

export function useCreateEdgeTypeField(edgeTypeId: string) {
  const qc = useQueryClient();
  return useMutation<EdgeTypeField, Error, CreateEdgeFieldRequest>({
    mutationFn: (req) => createEdgeTypeField(edgeTypeId, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["edge-types", "fields", edgeTypeId] })
  });
}

export function useUpdateEdgeTypeField(edgeTypeId: string) {
  const qc = useQueryClient();
  return useMutation<
    EdgeTypeField,
    Error,
    { fieldId: string; request: UpdateEdgeFieldRequest }
  >({
    mutationFn: ({ fieldId, request }) => updateEdgeTypeField(edgeTypeId, fieldId, request),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["edge-types", "fields", edgeTypeId] })
  });
}

export function useDeleteEdgeTypeField(edgeTypeId: string) {
  const qc = useQueryClient();
  return useMutation<void, Error, string>({
    mutationFn: (fieldId) => deleteEdgeTypeField(edgeTypeId, fieldId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["edge-types", "fields", edgeTypeId] })
  });
}

export function useCreateEdge(recordId: string) {
  const qc = useQueryClient();
  return useMutation<Edge, Error, CreateEdgeRequest>({
    mutationFn: createEdge,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["records", "edges", recordId] })
  });
}

export function useDeleteEdge(recordId: string) {
  const qc = useQueryClient();
  return useMutation<void, Error, string>({
    mutationFn: (edgeId) => deleteEdge(edgeId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["records", "edges", recordId] })
  });
}
