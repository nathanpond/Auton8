import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  getAdminSiteAppearance,
  getPublicSiteAppearance,
  updateSiteAppearance
} from "@/api/siteAppearance";
import { DEFAULT_SITE_APPEARANCE, coerceSiteAppearance } from "@/lib/siteAppearance";
import { SiteAppearance, UpdateSiteAppearanceRequest } from "@/types/siteAppearance";

export const PUBLIC_SITE_APPEARANCE_QUERY_KEY = ["site-appearance", "public"] as const;
export const ADMIN_SITE_APPEARANCE_QUERY_KEY = ["site-appearance", "admin"] as const;

export function usePublicSiteAppearance() {
  return useQuery<SiteAppearance>({
    queryKey: PUBLIC_SITE_APPEARANCE_QUERY_KEY,
    queryFn: ({ signal }) => getPublicSiteAppearance(signal),
    placeholderData: DEFAULT_SITE_APPEARANCE,
    select: coerceSiteAppearance
  });
}

export function useAdminSiteAppearance() {
  return useQuery<SiteAppearance>({
    queryKey: ADMIN_SITE_APPEARANCE_QUERY_KEY,
    queryFn: ({ signal }) => getAdminSiteAppearance(signal),
    placeholderData: DEFAULT_SITE_APPEARANCE,
    select: coerceSiteAppearance
  });
}

export function useUpdateSiteAppearance() {
  const qc = useQueryClient();

  return useMutation<SiteAppearance, Error, UpdateSiteAppearanceRequest>({
    mutationFn: updateSiteAppearance,
    onSuccess: (data) => {
      const normalized = coerceSiteAppearance(data);
      qc.setQueryData(PUBLIC_SITE_APPEARANCE_QUERY_KEY, normalized);
      qc.setQueryData(ADMIN_SITE_APPEARANCE_QUERY_KEY, normalized);
    }
  });
}
