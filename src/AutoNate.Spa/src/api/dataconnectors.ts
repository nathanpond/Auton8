import { api } from "./client";

export type DataConnector = {
  id: string;
  name: string;
  description: string | null;
  kind: string;
  configJson: string;
  lastFetchedAtUtc: string | null;
  cursor: string | null;
  ownerUserId: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  createdBy: string;
  updatedBy: string;
};

export type CreateDataConnectorRequest = {
  name: string;
  description?: string | null;
  kind: string;
  configJson?: string | null;
};

export type UpdateDataConnectorRequest = {
  name?: string;
  description?: string | null;
  configJson?: string | null;
};

export type ConnectorTestResult = {
  success: boolean;
  message: string;
  elapsed: string;
};

const BASE = "/api/dataconnectors";

export async function listDataConnectors(signal?: AbortSignal): Promise<DataConnector[]> {
  const res = await api.get<DataConnector[]>(BASE, { signal });
  return res.data;
}

export async function listDataConnectorKinds(signal?: AbortSignal): Promise<string[]> {
  const res = await api.get<string[]>(`${BASE}/kinds`, { signal });
  return res.data;
}

export async function getDataConnector(
  id: string,
  signal?: AbortSignal
): Promise<DataConnector> {
  const res = await api.get<DataConnector>(`${BASE}/${id}`, { signal });
  return res.data;
}

export async function createDataConnector(
  request: CreateDataConnectorRequest
): Promise<DataConnector> {
  const res = await api.post<DataConnector>(BASE, request);
  return res.data;
}

export async function updateDataConnector(
  id: string,
  request: UpdateDataConnectorRequest
): Promise<DataConnector> {
  const res = await api.put<DataConnector>(`${BASE}/${id}`, request);
  return res.data;
}

export async function deleteDataConnector(id: string): Promise<void> {
  await api.delete(`${BASE}/${id}`);
}

export async function testDataConnector(id: string): Promise<ConnectorTestResult> {
  const res = await api.post<ConnectorTestResult>(`${BASE}/${id}/test`);
  return res.data;
}

export type DataConnectorPreviewResult = {
  success: boolean;
  errorMessage: string | null;
  columns: string[];
  rows: Record<string, unknown>[];
};

export async function previewDataConnector(
  id: string,
  maxRows: number = 5
): Promise<DataConnectorPreviewResult> {
  const res = await api.post<DataConnectorPreviewResult>(`${BASE}/${id}/preview`, { maxRows });
  return res.data;
}
