import type { AdhocSubProcessState } from "@/api/executions";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { describeFreshness, type ExecutionFreshness } from "@/lib/executionFreshness";
import {
  addExecutionVariables,
  cancelExecution,
  completeTask,
  deleteAllExecutions,
  deleteExecution,
  forceCompleteTaskAtNode,
  getCompletedAssigneesForActivity,
  getExecutionDiagram,
  getExecutionActivityInstances,
  getExecutionHistory,
  getExecutionLog,
  getExecutionFreshness,
  getExecutionTasks,
  getTaskFormConfig,
  listExecutions,
  listExecutionsPage,
  ListExecutionsPageRequest,
  ListExecutionsPageResult,
  listMyAssignedTasks,
  listTeamAssignedTasks,
  moveExecutionState,
  reassignTaskAtNode,
  TaskFormConfig,
  updateExecutionVariables,
  updateTaskDueDateAtNode,
  ChildExecutionSummary,
  getExecutionChildren,
  getAdhocSubProcesses
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
export const TEAM_TASKS_QUERY_KEY = ["tasks", "assigned-to-team"] as const;

/**
 * The home panels' own keys (#268).
 *
 * MyTasksPanel and TeamTasksPanel do not query the two keys above. They merge
 * workflow tasks with assigned RECORDS, so they hold their own cache entries
 * under `["home", …]`, which share no prefix with `["tasks", …]` and are
 * therefore untouched by invalidating those.
 *
 * They live HERE, next to the mutations that have to invalidate them, rather
 * than in the panels that read them. #259 fixed the staleness by invalidating
 * from inside `MyTasksPanel.completeFromModal`, which was correct for that one
 * call site and left `TaskFormPage` -- the page the same panel navigates to for
 * a Page-mode task -- with the identical mismatch. A key a mutation must know
 * about cannot be a private detail of one component, or the next call site
 * inherits the bug by default.
 */
export const HOME_MY_TASKS_QUERY_KEY = ["home", "my-tasks"] as const;
export const HOME_TEAM_TASKS_QUERY_KEY = ["home", "team-tasks"] as const;

export function useExecutions() {
  return useQuery<WorkflowExecutionSummary[]>({
    queryKey: EXECUTIONS_QUERY_KEY,
    queryFn: ({ signal }) => listExecutions(signal)
  });
}

export const EXECUTION_FRESHNESS_QUERY_KEY = ["executions", "freshness"] as const;

/**
 * How current the executions view is (#109).
 *
 * THE REFETCH INTERVAL COMES FROM THE SERVER'S OWN POLL INTERVAL, not from a
 * number chosen here. That is the sixth acceptance criterion -- nothing polls
 * more aggressively than the configured interval just to make the indicator look
 * better -- and taking it from the response is what makes the criterion
 * structural rather than a promise. `describeFreshness` applies a floor so a
 * server reporting zero cannot turn this into a busy loop.
 */
export function useExecutionFreshness() {
  return useQuery<ExecutionFreshness>({
    queryKey: EXECUTION_FRESHNESS_QUERY_KEY,
    queryFn: ({ signal }) => getExecutionFreshness(signal),
    refetchInterval: (query) =>
      describeFreshness(query.state.data, Date.now()).refetchIntervalMs,
    // Not in the background: a hidden tab does not need to know, and polling it
    // is exactly the over-asking the criterion forbids.
    refetchIntervalInBackground: false
  });
}

export const executionsPageQueryKey = (req: ListExecutionsPageRequest) =>
  ["executions", "page", req] as const;

export function useExecutionsPage(req: ListExecutionsPageRequest, enabled = true) {
  return useQuery<ListExecutionsPageResult>({
    queryKey: executionsPageQueryKey(req),
    queryFn: ({ signal }) => listExecutionsPage(req, signal),
    enabled
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

// #173. The instances behind one collapsed multi-instance row.
//
// `enabled` is the lazy-loading criterion made structural: the query does not
// run until the row is expanded, and react-query then keeps the answer, so
// collapsing and reopening the same row does not re-fetch.
export const executionActivityInstancesQueryKey = (id: string, activityId: string) =>
  ["executions", "activity-instances", id, activityId] as const;

export function useExecutionActivityInstances(
  id: string | null,
  activityId: string | null,
  enabled: boolean
) {
  return useQuery<WorkflowExecutionHistoryEvent[]>({
    queryKey: executionActivityInstancesQueryKey(id ?? "unset", activityId ?? "unset"),
    queryFn: ({ signal }) =>
      id && activityId
        ? getExecutionActivityInstances(id, activityId, signal)
        : Promise.resolve([]),
    enabled: enabled && Boolean(id) && Boolean(activityId)
  });
}

export function useExecutionLog(id: string | null) {
  return useQuery<WorkflowExecutionLogEntry[]>({
    queryKey: executionLogQueryKey(id ?? "unset"),
    queryFn: ({ signal }) => (id ? getExecutionLog(id, signal) : Promise.resolve([])),
    enabled: Boolean(id)
  });
}

// #163.
export const adhocQueryKey = (id: string) => ["executions", "adhoc", id] as const;

export function useAdhocSubProcesses(id: string | null) {
  return useQuery<AdhocSubProcessState[]>({
    queryKey: adhocQueryKey(id ?? "unset"),
    queryFn: ({ signal }) => (id ? getAdhocSubProcesses(id, signal) : Promise.resolve([])),
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

// #113.
export function useExecutionChildren(id: string | null) {
  return useQuery<ChildExecutionSummary[]>({
    queryKey: ["execution-children", id ?? "unset"],
    queryFn: ({ signal }) => (id ? getExecutionChildren(id, signal) : Promise.resolve([])),
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
      qc.invalidateQueries({ queryKey: TEAM_TASKS_QUERY_KEY });
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
      qc.invalidateQueries({ queryKey: TEAM_TASKS_QUERY_KEY });
      // The home panels too (#268) -- every caller, not whichever one last had
      // the bug reported against it. Completing a task always makes both lists
      // wrong, wherever the completion was triggered from.
      qc.invalidateQueries({ queryKey: HOME_MY_TASKS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: HOME_TEAM_TASKS_QUERY_KEY });
    }
  });
}

export const taskFormConfigQueryKey = (taskId: string) =>
  ["tasks", "form-config", taskId] as const;

// Lazy lookup of a task's user-form configuration. Driven by a query so the
// SPA can call ensureQueryData on click (cache-first) without keeping it
// mounted, and also subscribe normally on the dedicated form page.
export function useTaskFormConfig(taskId: string | null) {
  return useQuery<TaskFormConfig | null>({
    queryKey: taskFormConfigQueryKey(taskId ?? "unset"),
    queryFn: ({ signal }) =>
      taskId ? getTaskFormConfig(taskId, signal) : Promise.resolve(null),
    enabled: Boolean(taskId)
  });
}

export function useMyAssignedTasks() {
  return useQuery<FlowableTaskSummary[]>({
    queryKey: ASSIGNED_TASKS_QUERY_KEY,
    queryFn: ({ signal }) => listMyAssignedTasks(signal)
  });
}

export function useTeamAssignedTasks() {
  return useQuery<FlowableTaskSummary[]>({
    queryKey: TEAM_TASKS_QUERY_KEY,
    queryFn: ({ signal }) => listTeamAssignedTasks(signal)
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
      qc.invalidateQueries({ queryKey: TEAM_TASKS_QUERY_KEY });
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
      qc.invalidateQueries({ queryKey: TEAM_TASKS_QUERY_KEY });
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
      qc.invalidateQueries({ queryKey: TEAM_TASKS_QUERY_KEY });
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
      qc.invalidateQueries({ queryKey: TEAM_TASKS_QUERY_KEY });
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
