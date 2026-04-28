import { useQuery } from "@tanstack/react-query";
import { getSystemHealth, SystemHealthReport } from "@/api/health";

export const SYSTEM_HEALTH_QUERY_KEY = ["health", "system"] as const;

// Polls the backend every 5 seconds so the SPA reflects flapping
// connections (e.g. the autonate-web Dapr sidecar's silent NATS-disconnect
// failure mode) without the user having to refresh.
export function useSystemHealth() {
  return useQuery<SystemHealthReport>({
    queryKey: SYSTEM_HEALTH_QUERY_KEY,
    queryFn: ({ signal }) => getSystemHealth(signal),
    refetchInterval: 5_000,
    staleTime: 0
  });
}
