import { api } from "./client";

export type AgentModel = {
  id: string;
  modelId: string;
  displayName: string;
  provider: string;
  contextWindowTokens: number;
  inputCostPerMillionTokens: number | null;
  outputCostPerMillionTokens: number | null;
  costCurrency: string;
  costPublishedAtUtc: string | null;
  description: string | null;
  isArchived: boolean;
  isDefault: boolean;
  isAvailable: boolean;
  // Computed by the API at list time. False when no enabled External
  // Connection exists for this model's provider — in which case the model
  // can't be set as default or marked available.
  providerHasConnection: boolean;
  sortOrder: number;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type RefreshResult = {
  providers: {
    provider: string;
    connectionKind: string;
    connectionId: string;
    providerModelCount: number;
    addedModelIds: string[];
    error: string | null;
  }[];
  skippedReasons: string[];
};

export type UpdateAgentModelRequest = {
  displayName?: string | null;
  provider?: string | null;
  contextWindowTokens?: number | null;
  inputCostPerMillionTokens?: number | null;
  outputCostPerMillionTokens?: number | null;
  costCurrency?: string | null;
  costPublishedAtUtc?: string | null;
  description?: string | null;
  sortOrder?: number | null;
};

const BASE = "/api/agent-models";

export async function listAgentModels(
  options?: { provider?: string },
  signal?: AbortSignal
): Promise<AgentModel[]> {
  const res = await api.get<AgentModel[]>(BASE, {
    params: { provider: options?.provider },
    signal
  });
  return res.data;
}

export async function updateAgentModel(id: string, request: UpdateAgentModelRequest): Promise<AgentModel> {
  const res = await api.put<AgentModel>(`${BASE}/${id}`, request);
  return res.data;
}

export async function setDefaultAgentModel(id: string): Promise<AgentModel> {
  const res = await api.post<AgentModel>(`${BASE}/${id}/set-default`);
  return res.data;
}

export async function setAgentModelAvailable(id: string): Promise<void> {
  await api.post(`${BASE}/${id}/set-available`);
}

export async function setAgentModelUnavailable(id: string): Promise<void> {
  await api.post(`${BASE}/${id}/set-unavailable`);
}

export async function refreshAgentModelCatalog(): Promise<RefreshResult> {
  const res = await api.post<RefreshResult>(`${BASE}/refresh`);
  return res.data;
}
