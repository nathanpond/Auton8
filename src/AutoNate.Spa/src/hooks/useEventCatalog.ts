import { useQuery } from "@tanstack/react-query";
import { EventCatalogResponse, getEventCatalog } from "@/api/eventCatalog";

export const EVENT_CATALOG_QUERY_KEY = ["event-catalog"] as const;

export function useEventCatalog() {
  return useQuery<EventCatalogResponse>({
    queryKey: EVENT_CATALOG_QUERY_KEY,
    queryFn: ({ signal }) => getEventCatalog(signal),
    staleTime: 60_000
  });
}
