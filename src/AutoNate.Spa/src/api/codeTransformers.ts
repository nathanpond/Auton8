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
};

export type UpdateCodeTransformerRequest = {
  name?: string;
  description?: string | null;
  code?: string;
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

export type TestCodeTransformerRequest = {
  // Override the stored row's code with the editor's current buffer so
  // the author can iterate without saving. Empty falls back to the
  // stored code.
  code?: string;
  config?: Record<string, string>;
  inputRows?: Record<string, unknown>[];
};

export type TestCodeTransformerResult = {
  success: boolean;
  errorMessage: string | null;
  outputRows: Record<string, unknown>[];
};

export async function testCodeTransformer(
  id: string,
  req: TestCodeTransformerRequest
): Promise<TestCodeTransformerResult> {
  const { data } = await api.post<TestCodeTransformerResult>(`${BASE}/${id}/test`, req);
  return data;
}
