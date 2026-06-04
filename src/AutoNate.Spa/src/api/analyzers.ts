import { AxiosError } from "axios";
import { api } from "./client";
import type { TransformerConfigSchema } from "./transformers";

export type AnalyzerCatalogEntry = {
  key: string;
  displayName: string;
};

// Analyzer schema is structurally identical to the transformer schema
// (same field-type vocabulary, same field shape) so we re-export the
// transformer type rather than duplicate it on the SPA.
export type AnalyzerConfigSchema = TransformerConfigSchema;

export async function listAnalyzers(signal?: AbortSignal): Promise<AnalyzerCatalogEntry[]> {
  const { data } = await api.get<AnalyzerCatalogEntry[]>("/api/analyzers/", { signal });
  return data;
}

export async function getAnalyzerSchema(
  key: string,
  signal?: AbortSignal
): Promise<AnalyzerConfigSchema | null> {
  try {
    const { data } = await api.get<AnalyzerConfigSchema>(
      `/api/analyzers/${encodeURIComponent(key)}/schema`,
      { signal }
    );
    return data;
  } catch (err) {
    if ((err as AxiosError)?.response?.status === 404) return null;
    throw err;
  }
}
