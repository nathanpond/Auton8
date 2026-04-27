import { useQuery } from "@tanstack/react-query";
import { listPageTemplates, PageTemplateInfo } from "@/api/pageTemplates";

export const PAGE_TEMPLATES_QUERY_KEY = ["page-templates"] as const;

export function usePageTemplates() {
  return useQuery<PageTemplateInfo[]>({
    queryKey: PAGE_TEMPLATES_QUERY_KEY,
    queryFn: ({ signal }) => listPageTemplates(signal),
    staleTime: 5 * 60_000
  });
}
