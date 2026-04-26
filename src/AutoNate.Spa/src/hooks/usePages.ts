import { useQuery } from "@tanstack/react-query";
import { listPages, lookupPage } from "@/api/pages";
import { PageContent, PageRegistryEntry } from "@/types/menus";

export const PAGES_QUERY_KEY = ["pages"] as const;
export const PAGE_QUERY_KEY = (path: string) => ["pages", "lookup", path] as const;

export function usePages() {
  return useQuery<PageRegistryEntry[]>({
    queryKey: PAGES_QUERY_KEY,
    queryFn: ({ signal }) => listPages(signal),
    staleTime: 60_000
  });
}

export function usePage(path: string | null) {
  return useQuery<PageContent | null>({
    queryKey: PAGE_QUERY_KEY(path ?? "unset"),
    queryFn: ({ signal }) => (path ? lookupPage(path, signal) : Promise.resolve(null)),
    enabled: Boolean(path),
    staleTime: 30_000
  });
}
