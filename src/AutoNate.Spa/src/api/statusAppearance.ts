import { api } from "./client";
import {
  CreateStatusAppearanceRequest,
  StatusAppearanceEntry,
  UpdateStatusAppearanceRequest
} from "@/types/statusAppearance";

const BASE = "/api/admin/status-appearance";

export async function listStatusAppearance(signal?: AbortSignal): Promise<StatusAppearanceEntry[]> {
  const { data } = await api.get<unknown>(BASE, { signal });

  if (Array.isArray(data)) {
    return data as StatusAppearanceEntry[];
  }

  if (data && typeof data === "object" && Array.isArray((data as { items?: unknown[] }).items)) {
    return (data as { items: StatusAppearanceEntry[] }).items;
  }

  return [];
}

export async function createStatusAppearance(
  request: CreateStatusAppearanceRequest
): Promise<StatusAppearanceEntry> {
  const { data } = await api.post<StatusAppearanceEntry>(BASE, request);
  return data;
}

export async function updateStatusAppearance(
  id: string,
  request: UpdateStatusAppearanceRequest
): Promise<StatusAppearanceEntry> {
  const { data } = await api.patch<StatusAppearanceEntry>(`${BASE}/${id}`, request);
  return data;
}

export async function deleteStatusAppearance(id: string): Promise<void> {
  await api.delete(`${BASE}/${id}`);
}
