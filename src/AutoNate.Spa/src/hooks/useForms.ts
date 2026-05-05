import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  CreateFormRequest,
  Form,
  FormDraftSnapshot,
  FormPublishedSnapshot,
  FormSummary,
  FormVersion,
  SaveFormRequest,
  createForm,
  deleteForm,
  getForm,
  getFormDevSnapshot,
  getFormPublishedSnapshot,
  listForms,
  listFormVersions,
  publishForm,
  restoreFormVersion,
  saveForm
} from "@/api/forms";

export const FORMS_QUERY_KEY = ["forms"] as const;
export const formQueryKey = (id: string) => ["forms", "detail", id] as const;
export const formVersionsQueryKey = (id: string) =>
  ["forms", "versions", id] as const;
export const formDevQueryKey = (shortCode: string) =>
  ["forms", "dev", shortCode] as const;
export const formPublishedQueryKey = (shortCode: string) =>
  ["forms", "public", shortCode] as const;

export function useForms() {
  return useQuery<FormSummary[]>({
    queryKey: FORMS_QUERY_KEY,
    queryFn: ({ signal }) => listForms(signal)
  });
}

export function useForm(id: string | null) {
  return useQuery<Form | null>({
    queryKey: formQueryKey(id ?? "unset"),
    queryFn: ({ signal }) => (id ? getForm(id, signal) : Promise.resolve(null)),
    enabled: Boolean(id)
  });
}

export function useFormVersions(id: string | null) {
  return useQuery<FormVersion[]>({
    queryKey: formVersionsQueryKey(id ?? "unset"),
    queryFn: ({ signal }) =>
      id ? listFormVersions(id, signal) : Promise.resolve([]),
    enabled: Boolean(id)
  });
}

// Polled query that drives /formdev/:shortCode. The 1s refetch keeps the
// preview tab nearly-live without wiring SSE.
export function useFormDevSnapshot(shortCode: string | null) {
  return useQuery<FormDraftSnapshot | null>({
    queryKey: formDevQueryKey(shortCode ?? "unset"),
    queryFn: ({ signal }) =>
      shortCode ? getFormDevSnapshot(shortCode, signal) : Promise.resolve(null),
    enabled: Boolean(shortCode),
    refetchInterval: 1000,
    refetchOnWindowFocus: true
  });
}

export function useFormPublishedSnapshot(shortCode: string | null) {
  return useQuery<FormPublishedSnapshot | null>({
    queryKey: formPublishedQueryKey(shortCode ?? "unset"),
    queryFn: ({ signal }) =>
      shortCode
        ? getFormPublishedSnapshot(shortCode, signal)
        : Promise.resolve(null),
    enabled: Boolean(shortCode)
  });
}

export function useCreateForm() {
  const qc = useQueryClient();
  return useMutation<Form, Error, CreateFormRequest>({
    mutationFn: (request) => createForm(request),
    onSuccess: (saved) => {
      qc.invalidateQueries({ queryKey: FORMS_QUERY_KEY });
      qc.setQueryData(formQueryKey(saved.id), saved);
    }
  });
}

export function useSaveForm() {
  const qc = useQueryClient();
  return useMutation<Form, Error, { id: string; request: SaveFormRequest }>({
    mutationFn: ({ id, request }) => saveForm(id, request),
    onSuccess: (saved) => {
      qc.invalidateQueries({ queryKey: FORMS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: formVersionsQueryKey(saved.id) });
      qc.setQueryData(formQueryKey(saved.id), saved);
    }
  });
}

export function useDeleteForm() {
  const qc = useQueryClient();
  return useMutation<void, Error, string>({
    mutationFn: (id) => deleteForm(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: FORMS_QUERY_KEY });
    }
  });
}

export function usePublishForm() {
  const qc = useQueryClient();
  return useMutation<Form, Error, string>({
    mutationFn: (id) => publishForm(id),
    onSuccess: (saved) => {
      qc.invalidateQueries({ queryKey: FORMS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: formVersionsQueryKey(saved.id) });
      qc.setQueryData(formQueryKey(saved.id), saved);
    }
  });
}

export function useRestoreFormVersion() {
  const qc = useQueryClient();
  return useMutation<
    Form,
    Error,
    { id: string; versionNumber: number }
  >({
    mutationFn: ({ id, versionNumber }) => restoreFormVersion(id, versionNumber),
    onSuccess: (saved) => {
      qc.invalidateQueries({ queryKey: FORMS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: formVersionsQueryKey(saved.id) });
      qc.setQueryData(formQueryKey(saved.id), saved);
    }
  });
}
