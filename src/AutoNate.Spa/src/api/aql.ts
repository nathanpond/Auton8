import { AxiosError } from "axios";
import { api } from "@/api/client";

export type AqlDataType = "string" | "number" | "bool" | "date" | "json";

export type AqlColumn = {
  name: string;
  dataType: AqlDataType;
};

export type AqlRow = Record<string, unknown>;

export type AqlQueryResponse = {
  columns: AqlColumn[];
  rows: AqlRow[];
  totalCount: number;
  truncated: boolean;
  durationMs: number;
};

export type AqlValidationError = {
  errors: string[];
};

export async function executeQuery(
  query: string,
  signal?: AbortSignal
): Promise<AqlQueryResponse> {
  const { data } = await api.post<AqlQueryResponse>(
    "/api/query",
    { query },
    { signal }
  );
  return data;
}

// Type guard for the 400-body shape the backend returns on validation
// failures so callers can show the bulleted error list inline.
export function isAqlValidationError(err: unknown): err is { errors: string[] } {
  if (!err || typeof err !== "object") return false;
  const e = err as { errors?: unknown };
  return Array.isArray(e.errors) && e.errors.every((x) => typeof x === "string");
}

export function extractValidationErrors(error: unknown): string[] | null {
  if (error instanceof AxiosError && error.response?.status === 400) {
    const body = error.response.data;
    if (isAqlValidationError(body)) return body.errors;
  }
  return null;
}
