import { api } from "./client";

export type WorkflowBehaviorCatalogEntry = {
  key: string;
  displayName: string;
  description: string | null;
};

export async function listWorkflowBehaviors(
  signal?: AbortSignal
): Promise<WorkflowBehaviorCatalogEntry[]> {
  const { data } = await api.get<WorkflowBehaviorCatalogEntry[]>("/api/workflow-behaviors", {
    signal
  });
  return data;
}
