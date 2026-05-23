import { useQuery } from "@tanstack/react-query";
import { listSavedQueries, type SavedQuery } from "@/api/savedQueries";

export const SAVED_QUERIES_QUERY_KEY = ["saved-queries"] as const;

export function useSavedQueries() {
  return useQuery<SavedQuery[]>({
    queryKey: SAVED_QUERIES_QUERY_KEY,
    queryFn: ({ signal }) => listSavedQueries(signal),
    staleTime: 30_000
  });
}
