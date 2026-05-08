import { api } from "./client";

export type SiteSettingType = "bool" | "string" | "int";
export type SiteSettingGroup = "general" | "features" | "chatbot";

export type SettingDefinition = {
  key: string;
  type: SiteSettingType;
  group: SiteSettingGroup;
  label: string;
  description: string;
  defaultValue: unknown;
  isPublic: boolean;
};

export type AdminSiteSettings = {
  definitions: SettingDefinition[];
  values: Record<string, unknown>;
};

export type PublicSiteSettings = Record<string, unknown>;

const PUBLIC_BASE = "/api/site-settings";
const ADMIN_BASE = "/api/admin/site-settings";

export async function getPublicSiteSettings(signal?: AbortSignal): Promise<PublicSiteSettings> {
  const { data } = await api.get<PublicSiteSettings>(PUBLIC_BASE, { signal });
  return data ?? {};
}

function coerceAdminSettings(data: unknown): AdminSiteSettings {
  // The endpoint can occasionally return a non-JSON body (e.g. when the auth
  // cookie expired and a misconfigured proxy/middleware sends index.html
  // instead of a 401). Guard the shape so callers see a clean failure rather
  // than "x is not iterable" deep in render.
  if (!data || typeof data !== "object") {
    throw new Error("Site settings endpoint returned an unexpected response.");
  }
  const obj = data as Partial<AdminSiteSettings>;
  if (!Array.isArray(obj.definitions) || typeof obj.values !== "object" || obj.values === null) {
    throw new Error("Site settings response is missing definitions or values.");
  }
  return { definitions: obj.definitions, values: obj.values };
}

export async function getAdminSiteSettings(signal?: AbortSignal): Promise<AdminSiteSettings> {
  const { data } = await api.get<unknown>(ADMIN_BASE, { signal });
  return coerceAdminSettings(data);
}

export async function updateSiteSettings(
  updates: Record<string, unknown>
): Promise<AdminSiteSettings> {
  const { data } = await api.patch<unknown>(ADMIN_BASE, { updates });
  return coerceAdminSettings(data);
}
