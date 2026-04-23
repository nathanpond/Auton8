import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  completeTask,
  deleteExecution,
  getExecutionDiagram,
  getExecutionTasks,
  listExecutions
} from "@/api/executions";
import {
  FlowableTaskSummary,
  WorkflowExecutionDiagramDetail,
  WorkflowExecutionSummary
} from "@/types/flowable";

export const EXECUTIONS_QUERY_KEY = ["executions"] as const;
export const executionDiagramQueryKey = (id: string) => ["executions", "diagram", id] as const;
export const executionTasksQueryKey = (id: string) => ["executions", "tasks", id] as const;

export function useExecutions() {
  return useQuery<WorkflowExecutionSummary[]>({
    queryKey: EXECUTIONS_QUERY_KEY,
    queryFn: ({ signal }) => listExecutions(signal)
  });
}

export function useExecutionDiagram(id: string | null) {
  return useQuery<WorkflowExecutionDiagramDetail | null>({
    queryKey: executionDiagramQueryKey(id ?? "unset"),
    queryFn: ({ signal }) =>
      id ? getExecutionDiagram(id, signal) : Promise.resolve(null),
    enabled: Boolean(id)
  });
}

export function useExecutionTasks(id: string | null) {
  return useQuery<FlowableTaskSummary[]>({
    queryKey: executionTasksQueryKey(id ?? "unset"),
    queryFn: ({ signal }) => (id ? getExecutionTasks(id, signal) : Promise.resolve([])),
    enabled: Boolean(id)
  });
}

export function useDeleteExecution() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (processInstanceId: string) => deleteExecution(processInstanceId),
    onSuccess: () => qc.invalidateQueries({ queryKey: EXECUTIONS_QUERY_KEY })
  });
}

export function useCompleteTask() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ taskId, variables }: { taskId: string; variables?: Record<string, unknown> }) =>
      completeTask(taskId, variables),
    onSuccess: () => qc.invalidateQueries({ queryKey: EXECUTIONS_QUERY_KEY })
  });
}
