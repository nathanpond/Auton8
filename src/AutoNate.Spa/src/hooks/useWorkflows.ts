import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  PublishResponse,
  getLatestWorkflow,
  getWorkflow,
  listWorkflows,
  listWorkflowVersions,
  publishWorkflow,
  saveWorkflow,
  startInstance
} from "@/api/workflows";
import {
  FlowableProcessInstanceSummary,
  WorkflowModel,
  WorkflowModelVersion
} from "@/types/flowable";

export const WORKFLOWS_QUERY_KEY = ["workflows"] as const;
export const WORKFLOW_LATEST_QUERY_KEY = ["workflows", "latest"] as const;
export const workflowQueryKey = (id: string) => ["workflows", "detail", id] as const;
export const workflowVersionsQueryKey = (id: string) => ["workflows", "versions", id] as const;

export function useWorkflows() {
  return useQuery<WorkflowModel[]>({
    queryKey: WORKFLOWS_QUERY_KEY,
    queryFn: ({ signal }) => listWorkflows(signal)
  });
}

export function useLatestWorkflow() {
  return useQuery<WorkflowModel | null>({
    queryKey: WORKFLOW_LATEST_QUERY_KEY,
    queryFn: ({ signal }) => getLatestWorkflow(signal)
  });
}

export function useWorkflow(id: string | null) {
  return useQuery<WorkflowModel | null>({
    queryKey: workflowQueryKey(id ?? "unset"),
    queryFn: ({ signal }) => (id ? getWorkflow(id, signal) : Promise.resolve(null)),
    enabled: Boolean(id)
  });
}

export function useWorkflowVersions(id: string | null) {
  return useQuery<WorkflowModelVersion[]>({
    queryKey: workflowVersionsQueryKey(id ?? "unset"),
    queryFn: ({ signal }) => (id ? listWorkflowVersions(id, signal) : Promise.resolve([])),
    enabled: Boolean(id)
  });
}

export function useSaveWorkflow() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (model: WorkflowModel) => saveWorkflow(model),
    onSuccess: (saved) => {
      qc.invalidateQueries({ queryKey: WORKFLOWS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: WORKFLOW_LATEST_QUERY_KEY });
      qc.setQueryData(workflowQueryKey(saved.id), saved);
    }
  });
}

export function usePublishWorkflow() {
  const qc = useQueryClient();
  return useMutation<PublishResponse, Error, WorkflowModel>({
    mutationFn: (model) => publishWorkflow(model),
    onSuccess: ({ model }) => {
      qc.invalidateQueries({ queryKey: WORKFLOWS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: workflowVersionsQueryKey(model.id) });
      qc.setQueryData(workflowQueryKey(model.id), model);
    }
  });
}

export function useStartInstance() {
  return useMutation<
    FlowableProcessInstanceSummary,
    Error,
    { processKey: string; variables?: Record<string, unknown> }
  >({
    mutationFn: ({ processKey, variables }) => startInstance(processKey, variables)
  });
}
