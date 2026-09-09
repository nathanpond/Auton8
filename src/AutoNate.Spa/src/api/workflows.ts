import { api } from "./client";
import {
  FlowableProcessInstanceSummary,
  WorkflowDeploymentInfo,
  WorkflowModel,
  WorkflowModelVersion
} from "@/types/flowable";

export async function listWorkflows(signal?: AbortSignal): Promise<WorkflowModel[]> {
  const { data } = await api.get<WorkflowModel[]>("/api/workflows", { signal });
  return data;
}

export async function getLatestWorkflow(signal?: AbortSignal): Promise<WorkflowModel | null> {
  try {
    const { data } = await api.get<WorkflowModel>("/api/workflows/latest", { signal });
    return data;
  } catch (error) {
    // 404 is the expected "no workflows yet" case.
    if (isNotFound(error)) {
      return null;
    }
    throw error;
  }
}

export async function getWorkflow(id: string, signal?: AbortSignal): Promise<WorkflowModel | null> {
  try {
    const { data } = await api.get<WorkflowModel>(`/api/workflows/${id}`, { signal });
    return data;
  } catch (error) {
    if (isNotFound(error)) {
      return null;
    }
    throw error;
  }
}

export async function listWorkflowVersions(
  id: string,
  signal?: AbortSignal
): Promise<WorkflowModelVersion[]> {
  const { data } = await api.get<WorkflowModelVersion[]>(`/api/workflows/${id}/versions`, {
    signal
  });
  return data;
}

export async function saveWorkflow(model: WorkflowModel): Promise<WorkflowModel> {
  const { data } = await api.post<WorkflowModel>("/api/workflows", model);
  return data;
}

export type WorkflowElementSnapshot = {
  id: string;
  type: string;
  name: string | null;
  scriptFormat?: string | null;
  script?: string | null;
  resultVariable?: string | null;
  conditionExpression?: string | null;
  assignee?: string | null;
  candidateUsers?: string[] | null;
  candidateGroups?: string[] | null;
  dueDate?: string | null;
  signalName?: string | null;
  signalTopic?: string | null;
  recordTypeShortCodes?: string[] | null;
  timerCycleCron?: string | null;
  timerEndDate?: string | null;
  timerDuration?: string | null;
  timerDate?: string | null;
  serviceTaskKind?: string | null;
  behaviorKey?: string | null;
  // #158. `conditionExpression` above carries a conditional event's condition too —
  // same concept as a sequence flow's, and `type` tells the two apart.
  cancelActivity?: boolean | null;
  // #157. Distinct from timerDuration/timerDate/timerCycleCron so a boundary event
  // cannot route to the start-event or intermediate-catch editors.
  boundaryTimerDuration?: string | null;
  boundaryTimerDate?: string | null;
  boundaryTimerCycle?: string | null;
  attachedTo?: string | null;
  // #168. Serialises to flowable:async on the activity — the step becomes its
  // own transaction boundary, so a failure retries it alone. Optional so an
  // older snapshot leaves an existing setting alone rather than clearing it.
  retryPoint?: boolean | null;
};

export type PrepareWorkflowRequest = {
  model: WorkflowModel;
  elementSnapshots: WorkflowElementSnapshot[];
};

export type PrepareWorkflowResponse = {
  model: WorkflowModel;
  warnings: string[];
  errors: string[];
};

export async function prepareWorkflow(
  request: PrepareWorkflowRequest
): Promise<PrepareWorkflowResponse> {
  const { data } = await api.post<PrepareWorkflowResponse>("/api/workflows/prepare", request);
  return data;
}

export type PublishResponse = {
  model: WorkflowModel;
  deployment: WorkflowDeploymentInfo;
};

export async function publishWorkflow(model: WorkflowModel): Promise<PublishResponse> {
  const { data } = await api.post<PublishResponse>(`/api/workflows/${model.id}/publish`, model);
  return data;
}

export async function startInstance(
  processKey: string,
  variables?: Record<string, unknown>
): Promise<FlowableProcessInstanceSummary> {
  const { data } = await api.post<FlowableProcessInstanceSummary>(
    `/api/workflows/${encodeURIComponent(processKey)}/start`,
    { variables: variables ?? null }
  );
  return data;
}

export async function pauseWorkflow(id: string): Promise<WorkflowModel> {
  const { data } = await api.post<WorkflowModel>(`/api/workflows/${id}/pause`);
  return data;
}

export async function resumeWorkflow(id: string): Promise<WorkflowModel> {
  const { data } = await api.post<WorkflowModel>(`/api/workflows/${id}/resume`);
  return data;
}

// Telemetry-only ping the Studio fires whenever the user opens a model in
// the modeler. Drives one ModelViewed audit event per switch — without this
// the studio loads every model in a single list call and the audit log only
// ever sees the list-view event. Errors are swallowed; missing telemetry
// must never break the modeler.
export async function markWorkflowViewed(id: string): Promise<void> {
  try {
    await api.post(`/api/workflows/${id}/viewed`);
  } catch {
    /* best-effort: don't surface telemetry failures to the user */
  }
}

function isNotFound(error: unknown): boolean {
  const response = (error as { response?: { status?: number } } | undefined)?.response;
  return response?.status === 404;
}

// #166. What a child process declares, so a call activity's mapping offers real
// targets instead of a free-text box the author must remember names for.
export type WorkflowDataDeclaration = {
  name: string;
  type: string | null;
  // "input" and "output" are an activity's contract; "variable" is a data object
  // or store. A parent maps INTO the child's inputs and OUT OF its outputs.
  kind: "input" | "output" | "variable";
};

export async function getWorkflowDeclarations(
  processKey: string,
  signal?: AbortSignal
): Promise<WorkflowDataDeclaration[]> {
  const { data } = await api.get<WorkflowDataDeclaration[]>(
    `/api/workflows/${encodeURIComponent(processKey)}/declarations`,
    { signal }
  );
  return data;
}
