import { AxiosError } from "axios";
import { api } from "./client";

// Phase 4 of the Data Stores plan — Transformer catalog API surface for the
// Phase 5 React Flow node palette. Per-key config schemas (audit fix #7)
// drive the editor's kind-specific form rendering.
export type TransformerCatalogEntry = {
  key: string;
  displayName: string;
  inputArity: number;
};

export type ConfigFieldSchema = {
  name: string;
  label: string;
  // Narrow vocabulary the editor maps to a Mantine control:
  // "text" → TextInput, "number" → NumberInput, "boolean" → Switch,
  // "select" → NativeSelect with options, "columns" → TextInput
  // (comma-separated; matches backend DataFrameOps.SplitColumnList).
  type: "text" | "number" | "boolean" | "select" | "columns";
  required: boolean;
  description: string | null;
  defaultValue: string | null;
  placeholder: string | null;
  options: string[] | null;
};

export type TransformerConfigSchema = {
  key: string;
  displayName: string;
  fields: ConfigFieldSchema[];
};

export async function listTransformers(signal?: AbortSignal): Promise<TransformerCatalogEntry[]> {
  const { data } = await api.get<TransformerCatalogEntry[]>("/api/transformers/", { signal });
  return data;
}

// Per-key schema. Plugin-contributed transformers don't have a schema
// today — the endpoint returns 404 and the editor falls back to its
// freeform JSON Textarea. Catching 404 here lets the React Query call
// site keep the result `null` instead of bubbling an error and
// flickering the editor into an alert state.
export async function getTransformerSchema(
  key: string,
  signal?: AbortSignal
): Promise<TransformerConfigSchema | null> {
  try {
    const { data } = await api.get<TransformerConfigSchema>(
      `/api/transformers/${encodeURIComponent(key)}/schema`,
      { signal }
    );
    return data;
  } catch (err) {
    if ((err as AxiosError)?.response?.status === 404) return null;
    throw err;
  }
}
