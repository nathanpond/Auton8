import { AxiosError } from "axios";
import { api } from "@/api/client";
import type { AqlQueryResponse } from "@/api/aql";

// Anonymous share-token redemption — calls the backend's
// /api/public/queries/share/{token} endpoint. Query parameters are
// forwarded as ?name=value pairs (the backend strips the leading `:`
// in either direction). The token itself goes in the path; never
// re-encoded into the query string.
export async function redeemSharedQuery(
  token: string,
  params: Record<string, string>,
  signal?: AbortSignal
): Promise<AqlQueryResponse> {
  const { data } = await api.get<AqlQueryResponse>(
    `/api/public/queries/share/${encodeURIComponent(token)}`,
    { params, signal }
  );
  return data;
}

// Walk the backend's 400-body `reason` for the placeholder name it
// names. AqlParameterBinder throws with a stable message shape
// (`Query parameter ':NAME' was referenced but not supplied.`) — the
// regex matches that one form. Returns null when the reason doesn't
// match (e.g. an AqlValidationException with a different cause), so
// the page can fall through to the generic-error UI.
const MISSING_PARAM_RE = /Query parameter ':([A-Za-z_][A-Za-z0-9_]*)'/;

export function extractMissingParamName(err: unknown): string | null {
  const axiosErr = err as AxiosError<{ reason?: string }> | undefined;
  if (!axiosErr?.response) return null;
  if (axiosErr.response.status !== 400) return null;
  const reason = axiosErr.response.data?.reason;
  if (typeof reason !== "string") return null;
  const match = MISSING_PARAM_RE.exec(reason);
  return match ? match[1] : null;
}

export function shareNotFound(err: unknown): boolean {
  return (err as AxiosError | undefined)?.response?.status === 404;
}
