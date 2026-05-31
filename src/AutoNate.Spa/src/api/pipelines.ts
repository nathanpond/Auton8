import { api } from "./client";

// Phase 5 of the Data Stores plan — Analytics Pipelines.
export type PipelineNodePosition = { x: number; y: number };

export type PipelineNode = {
  id: string;
  kind: "dataset-source" | "transformer" | "analyzer" | "dataset-sink";
  // For dataset-source / dataset-sink: dataset name.
  // For transformer / analyzer: registry key (e.g. "filter-rows").
  key: string;
  config?: Record<string, string> | null;
  position?: PipelineNodePosition | null;
};

export type PipelineEdge = {
  id: string;
  source: string;
  target: string;
};

export type PipelineGraph = {
  nodes: PipelineNode[];
  edges: PipelineEdge[];
};

export type Pipeline = {
  id: string;
  name: string;
  description: string | null;
  graphJson: string;
  scheduleCron: string | null;
  lastRunAtUtc: string | null;
  ownerUserId: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  createdBy: string;
  updatedBy: string;
};

export type PipelineRun = {
  id: string;
  pipelineId: string;
  status: "Queued" | "Running" | "Succeeded" | "Failed" | "Cancelled";
  graphSnapshotJson: string;
  queuedAtUtc: string;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  errorMessage: string | null;
  triggeredBy: string;
  triggerKind: string;
};

export type PipelineRunStep = {
  id: string;
  pipelineRunId: string;
  nodeKey: string;
  nodeKind: string;
  status: "Queued" | "Running" | "Succeeded" | "Failed" | "Cancelled";
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  rowCount: number | null;
  errorMessage: string | null;
};

export type PipelineRunDetail = {
  run: PipelineRun;
  steps: PipelineRunStep[];
};

export type CreatePipelineRequest = {
  name: string;
  description?: string | null;
  graph?: PipelineGraph;
  scheduleCron?: string | null;
};

export type UpdatePipelineRequest = {
  name?: string;
  description?: string | null;
  graph?: PipelineGraph;
  scheduleCron?: string | null;
};

const BASE = "/api/pipelines";

export async function listPipelines(signal?: AbortSignal): Promise<Pipeline[]> {
  const { data } = await api.get<Pipeline[]>(BASE, { signal });
  return data;
}

export async function getPipeline(id: string, signal?: AbortSignal): Promise<Pipeline> {
  const { data } = await api.get<Pipeline>(`${BASE}/${id}`, { signal });
  return data;
}

export async function createPipeline(req: CreatePipelineRequest): Promise<Pipeline> {
  const { data } = await api.post<Pipeline>(BASE, req);
  return data;
}

export async function updatePipeline(id: string, req: UpdatePipelineRequest): Promise<Pipeline> {
  const { data } = await api.put<Pipeline>(`${BASE}/${id}`, req);
  return data;
}

export async function deletePipeline(id: string): Promise<void> {
  await api.delete(`${BASE}/${id}`);
}

export async function runPipeline(id: string): Promise<PipelineRun> {
  const { data } = await api.post<PipelineRun>(`${BASE}/${id}/run`);
  return data;
}

export async function listPipelineRuns(id: string, signal?: AbortSignal): Promise<PipelineRun[]> {
  const { data } = await api.get<PipelineRun[]>(`${BASE}/${id}/runs`, { signal });
  return data;
}

export async function getPipelineRun(
  id: string,
  runId: string,
  signal?: AbortSignal
): Promise<PipelineRunDetail> {
  const { data } = await api.get<PipelineRunDetail>(`${BASE}/${id}/runs/${runId}`, { signal });
  return data;
}
