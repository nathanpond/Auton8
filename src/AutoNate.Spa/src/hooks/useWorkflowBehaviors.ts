import { useQuery } from "@tanstack/react-query";
import {
  WorkflowBehaviorCatalogEntry,
  listWorkflowBehaviors
} from "@/api/workflowBehaviors";

export const WORKFLOW_BEHAVIORS_QUERY_KEY = ["workflow-behaviors"] as const;

export function useWorkflowBehaviors() {
  return useQuery<WorkflowBehaviorCatalogEntry[]>({
    queryKey: WORKFLOW_BEHAVIORS_QUERY_KEY,
    queryFn: ({ signal }) => listWorkflowBehaviors(signal),
    staleTime: 60_000
  });
}
