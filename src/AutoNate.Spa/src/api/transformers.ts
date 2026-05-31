import { api } from "./client";

// Phase 4 of the Data Stores plan — Transformer catalog API surface for the
// Phase 5 React Flow node palette. v1 returns flat (key, displayName, arity)
// triples; per-transformer config schemas surface in a follow-up.
export type TransformerCatalogEntry = {
  key: string;
  displayName: string;
  inputArity: number;
};

export async function listTransformers(signal?: AbortSignal): Promise<TransformerCatalogEntry[]> {
  const { data } = await api.get<TransformerCatalogEntry[]>("/api/transformers/", { signal });
  return data;
}
