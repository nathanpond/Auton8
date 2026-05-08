import { api } from "./client";

export type ExternalConnectionKind =
  | "LlmProvider:Anthropic"
  | "LlmProvider:OpenAI";

export type ExternalConnectionMetadata = {
  baseUrl?: string;
  model?: string;
  maxTokens?: number;
  headers?: Record<string, string>;
  [key: string]: unknown;
};

export type ExternalConnection = {
  id: string;
  kind: string;
  name: string;
  description: string | null;
  isEnabled: boolean;
  isDefault: boolean;
  metadata: ExternalConnectionMetadata;
  secretFingerprint: string | null;
  createdAtUtc: string;
  createdBy: string;
  updatedAtUtc: string;
  updatedBy: string;
};

export type CreateExternalConnectionRequest = {
  kind: string;
  name: string;
  description?: string | null;
  isEnabled?: boolean;
  metadata?: ExternalConnectionMetadata;
  secret?: string | null;
};

export type UpdateExternalConnectionRequest = {
  name?: string;
  description?: string | null;
  isEnabled?: boolean;
  metadata?: ExternalConnectionMetadata;
  // Omit to keep existing; empty string to clear; non-empty to rotate.
  secret?: string | null;
};

export type TestConnectionResult = {
  ok: boolean;
  latencyMs: number;
  modelEcho: string | null;
  error: string | null;
};

const BASE = "/api/external-connections";

export async function listExternalConnections(
  kind?: string,
  signal?: AbortSignal
): Promise<ExternalConnection[]> {
  const res = await api.get<ExternalConnection[]>(BASE, {
    params: kind ? { kind } : undefined,
    signal
  });
  return res.data;
}

export async function getExternalConnection(
  id: string,
  signal?: AbortSignal
): Promise<ExternalConnection> {
  const res = await api.get<ExternalConnection>(`${BASE}/${id}`, { signal });
  return res.data;
}

export async function createExternalConnection(
  request: CreateExternalConnectionRequest
): Promise<ExternalConnection> {
  const res = await api.post<ExternalConnection>(BASE, request);
  return res.data;
}

export async function updateExternalConnection(
  id: string,
  request: UpdateExternalConnectionRequest
): Promise<ExternalConnection> {
  const res = await api.put<ExternalConnection>(`${BASE}/${id}`, request);
  return res.data;
}

export async function deleteExternalConnection(id: string): Promise<void> {
  await api.delete(`${BASE}/${id}`);
}

export async function testExternalConnection(id: string): Promise<TestConnectionResult> {
  const res = await api.post<TestConnectionResult>(`${BASE}/${id}/test`);
  return res.data;
}

export async function setDefaultExternalConnection(id: string): Promise<ExternalConnection> {
  const res = await api.post<ExternalConnection>(`${BASE}/${id}/set-default`);
  return res.data;
}
