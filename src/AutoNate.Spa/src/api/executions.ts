import { api } from "./client";
import {
  FlowableTaskSummary,
  WorkflowExecutionDiagramDetail,
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
