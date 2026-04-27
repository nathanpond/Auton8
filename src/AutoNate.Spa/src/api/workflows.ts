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

function isNotFound(error: unknown): boolean {
  const response = (error as { response?: { status?: number } } | undefined)?.response;
  return response?.status === 404;
}
