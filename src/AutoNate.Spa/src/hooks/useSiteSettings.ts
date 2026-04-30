import { useMemo } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AdminSiteSettings,
  PublicSiteSettings,
  getAdminSiteSettings,
  getPublicSiteSettings,
  updateSiteSettings
} from "@/api/siteSettings";

// Public setting keys that the SPA reads. Mirrors SiteSettingsKeys on the
// backend; keep them in sync when adding new flags.
export const SITE_SETTING_KEYS = {
  notificationsHeaderEnabled: "notifications.headerEnabled"
} as const;

// Built-in defaults applied client-side while the public-settings query is
// in-flight, so the first paint matches the eventual value for the common
// case where no admin override is set.
const DEFAULT_PUBLIC_VALUES: Record<string, unknown> = {
  [SITE_SETTING_KEYS.notificationsHeaderEnabled]: true
};

export const publicSiteSettingsKey = ["siteSettings", "public"] as const;
export const adminSiteSettingsKey = ["siteSettings", "admin"] as const;

export function usePublicSiteSettings() {
  const query = useQuery<PublicSiteSettings>({
    queryKey: publicSiteSettingsKey,
    queryFn: ({ signal }) => getPublicSiteSettings(signal),
    staleTime: 5 * 60_000
  });

  // Provide a stable accessor that falls back to defaults — keeps call sites
  // free of "if data, then check" boilerplate.
  return useMemo(() => {
    const values = query.data ?? {};
    return {
      ...query,
      getBool: (key: string, fallback?: boolean): boolean => {
        const v = values[key];
        if (typeof v === "boolean") return v;
        const def = DEFAULT_PUBLIC_VALUES[key];
        if (typeof def === "boolean") return def;
        return fallback ?? false;
      },
      getString: (key: string, fallback = ""): string => {
        const v = values[key];
        if (typeof v === "string") return v;
        const def = DEFAULT_PUBLIC_VALUES[key];
        if (typeof def === "string") return def;
        return fallback;
      }
    };
  }, [query]);
}

export function useAdminSiteSettings() {
  return useQuery<AdminSiteSettings>({
    queryKey: adminSiteSettingsKey,
    queryFn: ({ signal }) => getAdminSiteSettings(signal)
  });
}

export function useUpdateSiteSettings() {
  const qc = useQueryClient();
  return useMutation<AdminSiteSettings, Error, Record<string, unknown>>({
    mutationFn: (updates) => updateSiteSettings(updates),
    onSuccess: (data) => {
      qc.setQueryData(adminSiteSettingsKey, data);
      // Public settings could have changed — let it refetch.
      qc.invalidateQueries({ queryKey: publicSiteSettingsKey });
    }
  });
}
