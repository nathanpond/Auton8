import { api } from "./client";
import {
  FlowableTaskSummary,
  ProcessVariableUpdate,
  WorkflowExecutionDiagramDetail,
  WorkflowExecutionHistoryEvent,
  WorkflowExecutionLogEntry,
  WorkflowExecutionSummary
} from "@/types/flowable";

export async function listExecutions(signal?: AbortSignal): Promise<WorkflowExecutionSummary[]> {
  const { data } = await api.get<WorkflowExecutionSummary[]>("/api/executions", { signal });
  return data;
}

export async function getExecutionDiagram(
  processInstanceId: string,
  signal?: AbortSignal
): Promise<WorkflowExecutionDiagramDetail> {
  const { data } = await api.get<WorkflowExecutionDiagramDetail>(
    `/api/executions/${encodeURIComponent(processInstanceId)}/diagram`,
    { signal }
  );
  return data;
}

export async function getExecutionHistory(
  processInstanceId: string,
  signal?: AbortSignal
): Promise<WorkflowExecutionHistoryEvent[]> {
  const { data } = await api.get<WorkflowExecutionHistoryEvent[]>(
    `/api/executions/${encodeURIComponent(processInstanceId)}/history`,
    { signal }
  );
  return data;
}

export async function getExecutionLog(
  processInstanceId: string,
  signal?: AbortSignal
): Promise<WorkflowExecutionLogEntry[]> {
  const { data } = await api.get<WorkflowExecutionLogEntry[]>(
    `/api/executions/${encodeURIComponent(processInstanceId)}/log`,
    { signal }
  );
  return data;
}

export async function getExecutionTasks(
  processInstanceId: string,
  signal?: AbortSignal
): Promise<FlowableTaskSummary[]> {
  const { data } = await api.get<FlowableTaskSummary[]>(
    `/api/executions/${encodeURIComponent(processInstanceId)}/tasks`,
    { signal }
  );
  return data;
}

export async function deleteExecution(processInstanceId: string): Promise<void> {
  await api.delete(`/api/executions/${encodeURIComponent(processInstanceId)}`);
}

// Wipes every execution in Flowable — runtime + history. Returns the count
// the server reports as deleted so the UI can show it in a flash message.
// Gated on workflowexecution/deleteall.
export async function deleteAllExecutions(): Promise<{ deleted: number }> {
  const { data } = await api.post<{ deleted: number }>("/api/executions/delete-all");
  return data;
}

// Stops a running execution. The historic record stays so the row flips to
// "Cancelled" status. Gated on workflowexecution/cancel.
export async function cancelExecution(processInstanceId: string): Promise<void> {
  await api.post(`/api/executions/${encodeURIComponent(processInstanceId)}/cancel`);
}

export async function completeTask(
  taskId: string,
  variables?: Record<string, unknown>
): Promise<void> {
  await api.post(`/api/tasks/${encodeURIComponent(taskId)}/complete`, {
    variables: variables ?? null
  });
}

export async function listMyAssignedTasks(signal?: AbortSignal): Promise<FlowableTaskSummary[]> {
  const { data } = await api.get<FlowableTaskSummary[]>("/api/tasks/assigned-to-me", { signal });
  return data;
}

// Tasks the current actor can see — their own plus tasks of anyone they
// supervise. Acting on a task requires a separate permission check.
export async function listTasksVisibleToMe(signal?: AbortSignal): Promise<FlowableTaskSummary[]> {
  const { data } = await api.get<FlowableTaskSummary[]>("/api/tasks/visible-to-me", { signal });
  return data;
}

// Override-write process variables. Gated on workflowexecution/override.
export async function updateExecutionVariables(
  processInstanceId: string,
  variables: ProcessVariableUpdate[]
): Promise<void> {
  await api.put(`/api/executions/${encodeURIComponent(processInstanceId)}/variables`, {
    variables
  });
}

// Create new process variables. Flowable rejects POST against an existing
// name (409), so the SPA partitions adds vs. edits before calling this.
// Gated on workflowexecution/override.
export async function addExecutionVariables(
  processInstanceId: string,
  variables: ProcessVariableUpdate[]
): Promise<void> {
  await api.post(`/api/executions/${encodeURIComponent(processInstanceId)}/variables`, {
    variables
  });
}

// Override-complete a runtime task at a specific node, regardless of assignee.
// Gated on workflowexecution/override.
export async function forceCompleteTaskAtNode(
  processInstanceId: string,
  taskId: string,
  variables?: Record<string, unknown>
): Promise<void> {
  await api.post(
    `/api/executions/${encodeURIComponent(processInstanceId)}/tasks/${encodeURIComponent(taskId)}/force-complete`,
    { variables: variables ?? null }
  );
}

// Override-reassign a runtime task. Pass null/empty assignee to clear it.
// Gated on workflowexecution/override.
export async function reassignTaskAtNode(
  processInstanceId: string,
  taskId: string,
  assignee: string | null
): Promise<void> {
  await api.post(
    `/api/executions/${encodeURIComponent(processInstanceId)}/tasks/${encodeURIComponent(taskId)}/reassign`,
    { assignee: assignee && assignee.length > 0 ? assignee : null }
  );
}

// Override-set a task's due date. Pass null to clear. ISO 8601 string expected.
// Gated on workflowexecution/override.
export async function updateTaskDueDateAtNode(
  processInstanceId: string,
  taskId: string,
  dueDate: string | null
): Promise<void> {
  await api.post(
    `/api/executions/${encodeURIComponent(processInstanceId)}/tasks/${encodeURIComponent(taskId)}/due-date`,
    { dueDate: dueDate && dueDate.length > 0 ? dueDate : null }
  );
}

// Cancels every in-flight token on the execution and starts a fresh one at
// the target BPMN activity id. Gated on workflowexecution/movestate.
export async function moveExecutionState(
  processInstanceId: string,
  targetActivityId: string
): Promise<void> {
  await api.post(
    `/api/executions/${encodeURIComponent(processInstanceId)}/move-state`,
    { targetActivityId }
  );
}

export async function getCompletedAssigneesForActivity(
  processInstanceId: string,
  activityId: string,
  signal?: AbortSignal
): Promise<string[]> {
  const { data } = await api.get<string[]>(
    `/api/executions/${encodeURIComponent(processInstanceId)}/activities/${encodeURIComponent(activityId)}/completed-assignees`,
    { signal }
  );
  return data;
}
