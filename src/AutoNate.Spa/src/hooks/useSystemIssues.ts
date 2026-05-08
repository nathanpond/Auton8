import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  acknowledgeSystemIssue,
  getSystemIssue,
  resolveSystemIssue,
  SystemIssueModel
} from "@/api/systemIssues";

export const SYSTEM_ISSUES_QUERY_KEY = ["system-issues"] as const;

export function useSystemIssue(id: string | null) {
  return useQuery<SystemIssueModel>({
    queryKey: [...SYSTEM_ISSUES_QUERY_KEY, "detail", id],
    queryFn: ({ signal }) => getSystemIssue(id!, signal),
    enabled: !!id,
    staleTime: 0
  });
}

// Shared invalidator: any state mutation invalidates list + detail queries
// so the UI reflects the change without a manual refresh.
function useInvalidateSystemIssues() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: SYSTEM_ISSUES_QUERY_KEY });
}

export function useAcknowledgeSystemIssue() {
  const invalidate = useInvalidateSystemIssues();
  return useMutation({
    mutationFn: (id: string) => acknowledgeSystemIssue(id),
    onSuccess: invalidate
  });
}

export function useResolveSystemIssue() {
  const invalidate = useInvalidateSystemIssues();
  return useMutation({
    mutationFn: ({ id, notes }: { id: string; notes?: string }) => resolveSystemIssue(id, notes),
    onSuccess: invalidate
  });
}
