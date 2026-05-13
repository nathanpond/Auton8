import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  createStatusAppearance,
  deleteStatusAppearance,
  listStatusAppearance,
  reorderStatusAppearance,
  updateStatusAppearance
} from "@/api/statusAppearance";
import {
  CreateStatusAppearanceRequest,
  StatusAppearanceEntry,
  UpdateStatusAppearanceRequest
} from "@/types/statusAppearance";

export const STATUS_APPEARANCE_QUERY_KEY = ["status-appearance"] as const;

export function useStatusAppearance() {
  return useQuery<StatusAppearanceEntry[]>({
    queryKey: STATUS_APPEARANCE_QUERY_KEY,
    queryFn: ({ signal }) => listStatusAppearance(signal),
    placeholderData: []
  });
}

export function useCreateStatusAppearance() {
  const qc = useQueryClient();
  return useMutation<StatusAppearanceEntry, Error, CreateStatusAppearanceRequest>({
    mutationFn: createStatusAppearance,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: STATUS_APPEARANCE_QUERY_KEY });
    }
  });
}

export function useUpdateStatusAppearance() {
  const qc = useQueryClient();
  return useMutation<
    StatusAppearanceEntry,
    Error,
    { id: string; request: UpdateStatusAppearanceRequest }
  >({
    mutationFn: ({ id, request }) => updateStatusAppearance(id, request),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: STATUS_APPEARANCE_QUERY_KEY });
    }
  });
}

export function useDeleteStatusAppearance() {
  const qc = useQueryClient();
  return useMutation<void, Error, string>({
    mutationFn: deleteStatusAppearance,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: STATUS_APPEARANCE_QUERY_KEY });
    }
  });
}

export function useReorderStatusAppearance() {
  const qc = useQueryClient();
  return useMutation<void, Error, string[]>({
    mutationFn: reorderStatusAppearance,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: STATUS_APPEARANCE_QUERY_KEY });
    }
  });
}
