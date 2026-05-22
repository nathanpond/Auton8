import { api } from "./client";

const BASE = "/api/dashboards";

export type Dashboard = {
  id: string;
  ownerUserId: string;
  name: string;
  description: string | null;
  visibility: "private" | "shared" | "public";
  scope: "user" | "team" | "site";
  source: "user" | "template";
  templateKey: string | null;
  settings: Record<string, unknown>;
  isArchived: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  createdBy: string;
  updatedBy: string;
};

export type DashboardWidget = {
  id: string;
  dashboardId: string;
  widgetType: string;
  title: string | null;
  config: Record<string, unknown>;
  gridX: number;
  gridY: number;
  gridW: number;
  gridH: number;
  sortOrder: number;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type DashboardWithWidgets = {
  dashboard: Dashboard;
  widgets: DashboardWidget[];
};

export type CreateDashboardRequest = {
  name: string;
  description?: string | null;
  fromMountPath?: string | null;
};

export type UpdateDashboardRequest = {
  name?: string | null;
  description?: string | null;
  settings?: Record<string, unknown> | null;
};

export type CreateWidgetRequest = {
  widgetType: string;
  title?: string | null;
  config: Record<string, unknown>;
  gridX: number;
  gridY: number;
  gridW: number;
  gridH: number;
};

export type UpdateWidgetRequest = {
  title?: string | null;
  config?: Record<string, unknown> | null;
  gridX?: number | null;
  gridY?: number | null;
  gridW?: number | null;
  gridH?: number | null;
};

export type LayoutPositionDto = {
  widgetId: string;
  gridX: number;
  gridY: number;
  gridW: number;
  gridH: number;
};

export async function listDashboards(signal?: AbortSignal): Promise<Dashboard[]> {
  const { data } = await api.get<Dashboard[]>(`${BASE}/`, { signal });
  return data;
}

export async function getDashboard(id: string, signal?: AbortSignal): Promise<DashboardWithWidgets> {
  const { data } = await api.get<DashboardWithWidgets>(`${BASE}/${id}`, { signal });
  return data;
}

export async function createDashboard(request: CreateDashboardRequest): Promise<Dashboard> {
  const { data } = await api.post<Dashboard>(`${BASE}/`, request);
  return data;
}

export async function updateDashboard(id: string, request: UpdateDashboardRequest): Promise<Dashboard> {
  const { data } = await api.patch<Dashboard>(`${BASE}/${id}`, request);
  return data;
}

export async function deleteDashboard(id: string): Promise<void> {
  await api.delete(`${BASE}/${id}`);
}

export async function addWidget(dashboardId: string, request: CreateWidgetRequest): Promise<DashboardWidget> {
  const { data } = await api.post<DashboardWidget>(`${BASE}/${dashboardId}/widgets`, request);
  return data;
}

export async function updateWidget(
  dashboardId: string,
  widgetId: string,
  request: UpdateWidgetRequest
): Promise<DashboardWidget> {
  const { data } = await api.patch<DashboardWidget>(
    `${BASE}/${dashboardId}/widgets/${widgetId}`,
    request
  );
  return data;
}

export async function removeWidget(dashboardId: string, widgetId: string): Promise<void> {
  await api.delete(`${BASE}/${dashboardId}/widgets/${widgetId}`);
}

export async function replaceLayout(
  dashboardId: string,
  positions: LayoutPositionDto[]
): Promise<{ updated: number }> {
  const { data } = await api.post<{ updated: number }>(
    `${BASE}/${dashboardId}/layout`,
    { positions }
  );
  return data;
}
