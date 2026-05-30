import { api } from "./client";

// Phase 6 of the Data Stores plan — user-authored transformers / analyzers.
export type CodeTransformerKind = "transformer" | "analyzer";
export type CodeTransformerLanguage = "js" | "python";

export type CodeTransformer = {
  id: string;
  name: string;
  description: string | null;
  kind: CodeTransformerKind;
  language: CodeTransformerLanguage;
  code: string;
  isUnsafe: boolean;
  ownerUserId: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type CreateCodeTransformerRequest = {
  name: string;
  description?: string | null;
  kind: CodeTransformerKind;
  language: CodeTransformerLanguage;
  code: string;
  isUnsafe: boolean;
};

export type UpdateCodeTransformerRequest = {
  name?: string;
  description?: string | null;
  code?: string;
  isUnsafe?: boolean;
};

const BASE = "/api/code-transformers";

export async function listCodeTransformers(signal?: AbortSignal): Promise<CodeTransformer[]> {
  const { data } = await api.get<CodeTransformer[]>(BASE, { signal });
  return data;
}

export async function getCodeTransformer(id: string, signal?: AbortSignal): Promise<CodeTransformer> {
  const { data } = await api.get<CodeTransformer>(`${BASE}/${id}`, { signal });
  return data;
}

export async function createCodeTransformer(req: CreateCodeTransformerRequest): Promise<CodeTransformer> {
  const { data } = await api.post<CodeTransformer>(BASE, req);
  return data;
}

export async function updateCodeTransformer(
  id: string,
  req: UpdateCodeTransformerRequest
): Promise<CodeTransformer> {
  const { data } = await api.put<CodeTransformer>(`${BASE}/${id}`, req);
  return data;
}

export async function deleteCodeTransformer(id: string): Promise<void> {
  await api.delete(`${BASE}/${id}`);
}
