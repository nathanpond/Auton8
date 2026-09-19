import { api } from "./client";
import type { ExecutionFreshness } from "@/lib/executionFreshness";
import {
  FlowableTaskSummary,
  ProcessVariableUpdate,
  WorkflowExecutionDiagramDetail,
  WorkflowExecutionHistoryEvent,
  WorkflowExecutionLogEntry,
  WorkflowExecutionSummary
} from "@/types/flowable";

/** How current the executions view is, and whether it is still updating (#594). */
export async function getExecutionFreshness(
  signal?: AbortSignal
): Promise<ExecutionFreshness> {
  const { data } = await api.get<ExecutionFreshness>("/api/executions/freshness", { signal });
  return data;
}

export async function listExecutions(signal?: AbortSignal): Promise<WorkflowExecutionSummary[]> {
  const { data } = await api.get<WorkflowExecutionSummary[]>("/api/executions", { signal });
  return data;
}

export type ListExecutionsPageRequest = {
  page: number;
  pageSize: number;
  search?: string;
  sort?: string;
  sortDir?: "asc" | "desc";
  status?: string;
  workflowModelId?: string;
};

export type ListExecutionsPageResult = {
  items: WorkflowExecutionSummary[];
  totalCount: number;
};

export async function listExecutionsPage(
  req: ListExecutionsPageRequest,
  signal?: AbortSignal
): Promise<ListExecutionsPageResult> {
  const params: Record<string, string | number> = {
    page: req.page,
    pageSize: req.pageSize
  };
  if (req.search) params.q = req.search;
  if (req.sort) params.sort = req.sort;
  if (req.sortDir) params.sortDir = req.sortDir;
  if (req.status) params.status = req.status;
  if (req.workflowModelId) params.workflowModelId = req.workflowModelId;
  const { data } = await api.get<ListExecutionsPageResult>("/api/executions/page", {
    params,
    signal
  });
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

// #113. The child instances a call activity in this execution started. The
// engine records the relationship; without surfacing it a waiting call activity
// looks like a hung process, because the parent's own task list is empty.
export type ChildExecutionSummary = {
  id: string;
  name: string | null;
  processDefinitionId: string;
  activityId: string | null;
  suspended: boolean;
  startUserId: string | null;
};

export async function getExecutionChildren(
  processInstanceId: string,
  signal?: AbortSignal
): Promise<ChildExecutionSummary[]> {
  const { data } = await api.get<ChildExecutionSummary[]>(
    `/api/executions/${encodeURIComponent(processInstanceId)}/children`,
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

export type TaskFormMode = "simple" | "modal" | "page";

export type TaskFormWorkflowSnapshot = {
  formId: string;
  name: string;
  shortCode: string;
  formCode: string;
  publishedVersionNumber: number | null;
  isDraftFallback: boolean;
};

export type GatewayChoice = {
  flowId: string;
  label: string;
  description: string | null;
};

export type TaskFormConfig = {
  taskId: string;
  taskName: string;
  taskDefinitionKey: string | null;
  processInstanceId: string | null;
  processInstanceName: string | null;
  processDefinitionName: string | null;
  mode: TaskFormMode;
  formShortCode: string | null;
  form: TaskFormWorkflowSnapshot | null;
  variables: Record<string, unknown>;
  description: string | null;
  gatewayChoices: GatewayChoice[] | null;
};

// Reserved variable name. Mirrors WorkflowBpmnXml.GatewayChoiceVariableName on
// the backend — synthetic conditions injected at publish time read this
// variable to route through gateways after default-mode user tasks.
export const GATEWAY_CHOICE_VARIABLE = "__autonateChosenFlow";

export async function getTaskFormConfig(
  taskId: string,
  signal?: AbortSignal
): Promise<TaskFormConfig | null> {
  try {
    const { data } = await api.get<TaskFormConfig>(
      `/api/tasks/${encodeURIComponent(taskId)}/form-config`,
      { signal }
    );
    return data;
  } catch (error) {
    const status = (error as { response?: { status?: number } }).response?.status;
    if (status === 404) return null;
    throw error;
  }
}

export async function listMyAssignedTasks(signal?: AbortSignal): Promise<FlowableTaskSummary[]> {
  const { data } = await api.get<FlowableTaskSummary[]>("/api/tasks/assigned-to-me", { signal });
  return data;
}

// Tasks assigned to anyone the current actor supervises (excludes their own
// tasks). Used by the home Team Tasks panel; acting on these from here is
// not supported — supervisors go to the execution viewer to take override
// actions.
export async function listTeamAssignedTasks(signal?: AbortSignal): Promise<FlowableTaskSummary[]> {
  const { data } = await api.get<FlowableTaskSummary[]>("/api/tasks/assigned-to-team", { signal });
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

// #163. An ad-hoc subprocess holds activities with no predetermined order: the
// process says what CAN be done and a person decides what happens next.
export type AdhocActivity = {
  id: string;
  name: string | null;
};

export type AdhocSubProcessState = {
  // The ENGINE execution, not the activity — there can be more than one live
  // instance of the same ad-hoc subprocess, each with its own enabled set.
  executionId: string;
  activityId: string;
  enabledActivities: AdhocActivity[];
};

export async function getAdhocSubProcesses(
  processInstanceId: string,
  signal?: AbortSignal
): Promise<AdhocSubProcessState[]> {
  const { data } = await api.get<AdhocSubProcessState[]>(
    `/api/executions/${encodeURIComponent(processInstanceId)}/adhoc`,
    { signal }
  );
  return data;
}

// Starting the same activity twice is legitimate and runs it twice — that
// repeatability is what distinguishes an ad-hoc subprocess from a parallel one.
export async function startAdhocActivity(
  processInstanceId: string,
  executionId: string,
  activityId: string
): Promise<void> {
  await api.post(
    `/api/executions/${encodeURIComponent(processInstanceId)}/adhoc/` +
      `${encodeURIComponent(executionId)}/activities/${encodeURIComponent(activityId)}`
  );
}

export async function completeAdhocSubProcess(
  processInstanceId: string,
  executionId: string
): Promise<void> {
  await api.post(
    `/api/executions/${encodeURIComponent(processInstanceId)}/adhoc/` +
      `${encodeURIComponent(executionId)}/complete`
  );
}

// ── Jobs and timers (#172) ───────────────────────────────────────────────────
//
// Read LIVE from the engine, not from workflow_execution_cache. The question an
// operator opens these to answer is "what is stuck right now", and a cache would
// put a staleness question in front of exactly the reader who cannot tolerate
// one. That is the deliberate difference from the executions list, which #104
// serves from the cache.

/** Which of the engine's four collections a job is sitting in. */
export type WorkflowJobQueue = "Executable" | "Timer" | "DeadLetter" | "Suspended";

export type WorkflowJob = {
  id: string;
  queue: WorkflowJobQueue;
  processInstanceId: string | null;
  processDefinitionId: string | null;
  /** The BPMN element this job belongs to — the step an operator recognises. */
  elementId: string | null;
  elementName: string | null;
  /** Attempts remaining. Zero on a dead-lettered job, which is what put it there. */
  retries: number;
  exceptionMessage: string | null;
  /** When a timer will fire, or when a retrying job is next due. */
  dueAtUtc: string | null;
  createdAtUtc: string | null;
};

export async function listExecutionJobs(
  processInstanceId: string,
  signal?: AbortSignal
): Promise<WorkflowJob[]> {
  const { data } = await api.get<WorkflowJob[]>(
    `/api/executions/${encodeURIComponent(processInstanceId)}/jobs`,
    { signal }
  );
  return data;
}

/**
 * Every stuck job, across every execution the caller may see.
 *
 * Defaults to dead-lettered only. A list that also carried every healthy
 * scheduled timer would bury the thing it exists to surface.
 */
export async function listStuckJobs(
  all: boolean,
  signal?: AbortSignal
): Promise<WorkflowJob[]> {
  const { data } = await api.get<WorkflowJob[]>(
    `/api/executions/jobs${all ? "?all=true" : ""}`,
    { signal }
  );
  return data;
}

/**
 * The stack the engine retained, or null when it kept none.
 *
 * Null is a real answer, not a failure: a job can be dead-lettered with a
 * message and no stack.
 */
export async function getJobExceptionStack(
  processInstanceId: string,
  jobId: string,
  queue: WorkflowJobQueue,
  signal?: AbortSignal
): Promise<string | null> {
  const { data } = await api.get<{ jobId: string; stack: string | null }>(
    `/api/executions/${encodeURIComponent(processInstanceId)}/jobs/` +
      `${encodeURIComponent(jobId)}/exception?queue=${encodeURIComponent(queue)}`,
    { signal }
  );
  return data.stack;
}

/**
 * Puts a failed or dead-lettered job back in front of the engine.
 *
 * Resolving means the job is QUEUED again, not that the step succeeded — for a
 * dead-lettered job the engine performs a move, and the async executor picks it
 * up afterwards. Callers report what was asked for, not what happened.
 */
export async function retryJob(
  processInstanceId: string,
  jobId: string,
  queue: WorkflowJobQueue
): Promise<void> {
  await api.post(
    `/api/executions/${encodeURIComponent(processInstanceId)}/jobs/${encodeURIComponent(jobId)}/retry`,
    { queue }
  );
}

export async function rescheduleJob(
  processInstanceId: string,
  jobId: string,
  dueAtUtc: string
): Promise<void> {
  await api.post(
    `/api/executions/${encodeURIComponent(processInstanceId)}/jobs/${encodeURIComponent(jobId)}/reschedule`,
    { dueAtUtc }
  );
}
