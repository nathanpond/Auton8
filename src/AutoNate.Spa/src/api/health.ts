import { api } from "./client";

export async function getDaprSidecarStatus(signal?: AbortSignal): Promise<{ available: boolean }> {
  const { data } = await api.get<{ available: boolean }>("/api/health/dapr", { signal });
  return data;
}
