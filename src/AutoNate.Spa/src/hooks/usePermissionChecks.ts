import { useQuery } from "@tanstack/react-query";
import {
  PermissionCheck,
  PermissionCheckResult,
  checkPermissions
} from "@/api/auth";

// Stable cache key for a list of (kind, action, id) tuples. Sorting before
// joining avoids re-fetching when the same set arrives in a different order.
function buildKey(checks: PermissionCheck[]): string {
  return checks
    .map((c) => `${c.kind}|${c.action}|${c.id}`)
    .sort()
    .join(";");
}

// Batched permission lookup. The query key dedupes by the set of tuples, so
// re-rendering with the same checks doesn't hit the network. Result is a
// `Map<"kind|action|id", boolean>` for cheap lookup at render time.
export function usePermissionChecks(checks: PermissionCheck[]) {
  return useQuery<Map<string, boolean>>({
    queryKey: ["auth", "checks", buildKey(checks)],
    queryFn: async ({ signal }) => {
      const results = await checkPermissions(checks, signal);
      const map = new Map<string, boolean>();
      for (const r of results) {
        map.set(`${r.kind}|${r.action}|${r.id}`, r.allowed);
      }
      return map;
    },
    enabled: checks.length > 0,
    staleTime: 30 * 1000
  });
}

// Convenience: build the lookup key the same way as the hook so callers
// don't have to know the format.
export function permissionKey(check: PermissionCheck): string {
  return `${check.kind}|${check.action}|${check.id}`;
}
