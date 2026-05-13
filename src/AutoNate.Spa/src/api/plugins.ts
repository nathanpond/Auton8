import { api } from "./client";

export type PluginStatus = "Disabled" | "Enabled" | "DeletedPending";

export type Plugin = {
  id: string;
  name: string;
  version: string;
  status: PluginStatus;
  uploadedAt: string;
  uploadedBy: string;
  lastEnabledAt: string | null;
  lastDisabledAt: string | null;
  lastError: string | null;
};

// Server returns Status as the int from the enum; map to a string the UI can
// switch on without remembering the numeric ordering.
const STATUS_BY_NUMBER: Record<number, PluginStatus> = {
  0: "Disabled",
  1: "Enabled",
  2: "DeletedPending",
};

function normalize(p: Plugin & { status: PluginStatus | number }): Plugin {
  const status = typeof p.status === "number" ? STATUS_BY_NUMBER[p.status] : p.status;
  return { ...p, status };
}

export async function listPlugins(signal?: AbortSignal): Promise<Plugin[]> {
  const { data } = await api.get<Array<Plugin & { status: PluginStatus | number }>>(
    "/api/admin/plugins", { signal });
  return data.map(normalize);
}

export async function getPlugin(id: string, signal?: AbortSignal): Promise<Plugin> {
  const { data } = await api.get<Plugin & { status: PluginStatus | number }>(
    `/api/admin/plugins/${id}`, { signal });
  return normalize(data);
}

export async function uploadPlugin(file: File): Promise<Plugin> {
  const form = new FormData();
  form.append("file", file);
  const { data } = await api.post<Plugin & { status: PluginStatus | number }>(
    "/api/admin/plugins", form, {
      headers: { "Content-Type": "multipart/form-data" },
    });
  return normalize(data);
}

export async function updatePlugin(id: string, file: File): Promise<Plugin> {
  const form = new FormData();
  form.append("file", file);
  const { data } = await api.post<Plugin & { status: PluginStatus | number }>(
    `/api/admin/plugins/${id}/update`, form, {
      headers: { "Content-Type": "multipart/form-data" },
    });
  return normalize(data);
}

export async function enablePlugin(id: string): Promise<Plugin> {
  const { data } = await api.post<Plugin & { status: PluginStatus | number }>(
    `/api/admin/plugins/${id}/enable`);
  return normalize(data);
}

export async function disablePlugin(id: string): Promise<Plugin> {
  const { data } = await api.post<Plugin & { status: PluginStatus | number }>(
    `/api/admin/plugins/${id}/disable`);
  return normalize(data);
}

export async function deletePlugin(id: string): Promise<void> {
  await api.delete(`/api/admin/plugins/${id}`);
}
