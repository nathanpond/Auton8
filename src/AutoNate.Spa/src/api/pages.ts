import { api } from "./client";
import { PageContent, PageRegistryEntry } from "@/types/menus";

const BASE = "/api/pages";

export async function listPages(signal?: AbortSignal): Promise<PageRegistryEntry[]> {
  const { data } = await api.get<PageRegistryEntry[]>(BASE, { signal });
  return data;
}

export async function lookupPage(
  path: string,
  signal?: AbortSignal
): Promise<PageContent | null> {
  try {
    const { data } = await api.get<PageContent>(`${BASE}/lookup`, {
      params: { path },
      signal
    });
    return data;
  } catch (error) {
    if (isNotFound(error)) return null;
    throw error;
  }
}

function isNotFound(error: unknown): boolean {
  const response = (error as { response?: { status?: number } } | undefined)?.response;
  return response?.status === 404;
}
