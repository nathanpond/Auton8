import { api } from "./client";

export async function getDaprSidecarStatus(signal?: AbortSignal): Promise<{ available: boolean }> {
  const { data } = await api.get<{ available: boolean }>("/api/health/dapr", { signal });
  return data;
}

export type HealthStatus = "Up" | "Down" | "Degraded" | "Unknown";

export type ComponentHealth = {
  id: string;
  name: string;
  kind: string;
  status: HealthStatus;
  message: string | null;
  details: Record<string, string> | null;
  latencyMs: number | null;
};

export type ConnectionHealth = {
  from: string;
  to: string;
  label: string;
  status: HealthStatus;
  message: string | null;
  latencyMs: number | null;
};

export type SystemHealthReport = {
  checkedAtUtc: string;
  components: ComponentHealth[];
  connections: ConnectionHealth[];
};

export async function getSystemHealth(signal?: AbortSignal): Promise<SystemHealthReport> {
  const { data } = await api.get<SystemHealthReport>("/api/health/system", { signal });
  return data;
}
