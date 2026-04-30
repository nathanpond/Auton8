import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  addExecutionVariables,
  cancelExecution,
  completeTask,
  deleteAllExecutions,
  deleteExecution,
  forceCompleteTaskAtNode,
  getCompletedAssigneesForActivity,
  getExecutionDiagram,
  getExecutionHistory,
  getExecutionLog,
  getExecutionTasks,
  listExecutions,
  listMyAssignedTasks,
  listTasksVisibleToMe,
  moveExecutionState,
  reassignTaskAtNode,
  updateExecutionVariables,
  updateTaskDueDateAtNode
} from "@/api/executions";
import {
  FlowableTaskSummary,
  ProcessVariableUpdate,
  WorkflowExecutionDiagramDetail,
  WorkflowExecutionHistoryEvent,
  WorkflowExecutionLogEntry,
  WorkflowExecutionSummary
} from "@/types/flowable";

export const EXECUTIONS_QUERY_KEY = ["executions"] as const;
export const executionDiagramQueryKey = (id: string) => ["executions", "diagram", id] as const;
export const executionHistoryQueryKey = (id: string) => ["executions", "history", id] as const;
export const executionLogQueryKey = (id: string) => ["executions", "log", id] as const;
export const executionTasksQueryKey = (id: string) => ["executions", "tasks", id] as const;
export const ASSIGNED_TASKS_QUERY_KEY = ["tasks", "assigned-to-me"] as const;
export const VISIBLE_TASKS_QUERY_KEY = ["tasks", "visible-to-me"] as const;

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

export function useExecutionHistory(id: string | null) {
  return useQuery<WorkflowExecutionHistoryEvent[]>({
    queryKey: executionHistoryQueryKey(id ?? "unset"),
    queryFn: ({ signal }) => (id ? getExecutionHistory(id, signal) : Promise.resolve([])),
    enabled: Boolean(id)
  });
}

export function useExecutionLog(id: string | null) {
  return useQuery<WorkflowExecutionLogEntry[]>({
    queryKey: executionLogQueryKey(id ?? "unset"),
    queryFn: ({ signal }) => (id ? getExecutionLog(id, signal) : Promise.resolve([])),
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

export function useDeleteAllExecutions() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => deleteAllExecutions(),
    onSuccess: () => qc.invalidateQueries({ queryKey: EXECUTIONS_QUERY_KEY })
  });
}

export function useCancelExecution() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (processInstanceId: string) => cancelExecution(processInstanceId),
    onSuccess: (_data, processInstanceId) => {
      qc.invalidateQueries({ queryKey: EXECUTIONS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: executionDiagramQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: executionHistoryQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: executionLogQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: executionTasksQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: ASSIGNED_TASKS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: VISIBLE_TASKS_QUERY_KEY });
    }
  });
}

export function useCompleteTask() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ taskId, variables }: { taskId: string; variables?: Record<string, unknown> }) =>
      completeTask(taskId, variables),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: EXECUTIONS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: ASSIGNED_TASKS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: VISIBLE_TASKS_QUERY_KEY });
    }
  });
}

export function useMyAssignedTasks() {
  return useQuery<FlowableTaskSummary[]>({
    queryKey: ASSIGNED_TASKS_QUERY_KEY,
    queryFn: ({ signal }) => listMyAssignedTasks(signal)
  });
}

export function useTasksVisibleToMe() {
  return useQuery<FlowableTaskSummary[]>({
    queryKey: VISIBLE_TASKS_QUERY_KEY,
    queryFn: ({ signal }) => listTasksVisibleToMe(signal)
  });
}

export const completedAssigneesQueryKey = (processInstanceId: string, activityId: string) =>
  ["executions", "completed-assignees", processInstanceId, activityId] as const;

export function useUpdateExecutionVariables(processInstanceId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (variables: ProcessVariableUpdate[]) =>
      updateExecutionVariables(processInstanceId, variables),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: executionDiagramQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: EXECUTIONS_QUERY_KEY });
    }
  });
}

export function useAddExecutionVariables(processInstanceId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (variables: ProcessVariableUpdate[]) =>
      addExecutionVariables(processInstanceId, variables),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: executionDiagramQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: executionLogQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: EXECUTIONS_QUERY_KEY });
    }
  });
}

export function useForceCompleteTask(processInstanceId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ taskId, variables }: { taskId: string; variables?: Record<string, unknown> }) =>
      forceCompleteTaskAtNode(processInstanceId, taskId, variables),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: executionDiagramQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: executionHistoryQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: executionLogQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: executionTasksQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: EXECUTIONS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: ASSIGNED_TASKS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: VISIBLE_TASKS_QUERY_KEY });
    }
  });
}

export function useReassignTask(processInstanceId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ taskId, assignee }: { taskId: string; assignee: string | null }) =>
      reassignTaskAtNode(processInstanceId, taskId, assignee),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: executionLogQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: executionTasksQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: ASSIGNED_TASKS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: VISIBLE_TASKS_QUERY_KEY });
    }
  });
}

export function useUpdateTaskDueDate(processInstanceId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ taskId, dueDate }: { taskId: string; dueDate: string | null }) =>
      updateTaskDueDateAtNode(processInstanceId, taskId, dueDate),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: executionLogQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: executionTasksQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: ASSIGNED_TASKS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: VISIBLE_TASKS_QUERY_KEY });
    }
  });
}

export function useMoveExecutionState(processInstanceId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (targetActivityId: string) =>
      moveExecutionState(processInstanceId, targetActivityId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: executionDiagramQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: executionHistoryQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: executionLogQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: executionTasksQueryKey(processInstanceId) });
      qc.invalidateQueries({ queryKey: EXECUTIONS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: ASSIGNED_TASKS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: VISIBLE_TASKS_QUERY_KEY });
    }
  });
}

export function useCompletedAssigneesForActivity(
  processInstanceId: string | null,
  activityId: string | null,
  enabled: boolean
) {
  return useQuery<string[]>({
    queryKey: completedAssigneesQueryKey(processInstanceId ?? "unset", activityId ?? "unset"),
    queryFn: ({ signal }) =>
      processInstanceId && activityId
        ? getCompletedAssigneesForActivity(processInstanceId, activityId, signal)
        : Promise.resolve([]),
    enabled: enabled && Boolean(processInstanceId) && Boolean(activityId)
  });
}
