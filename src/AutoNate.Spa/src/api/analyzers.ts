import { api } from "./client";

export type AnalyzerCatalogEntry = {
  key: string;
  displayName: string;
};

export async function listAnalyzers(signal?: AbortSignal): Promise<AnalyzerCatalogEntry[]> {
  const { data } = await api.get<AnalyzerCatalogEntry[]>("/api/analyzers/", { signal });
  return data;
}
