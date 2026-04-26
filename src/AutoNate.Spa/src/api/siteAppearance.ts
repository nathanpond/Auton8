import { api } from "./client";
import { SiteAppearance, UpdateSiteAppearanceRequest } from "@/types/siteAppearance";

const PUBLIC_BASE = "/api/appearance";
const ADMIN_BASE = "/api/admin/appearance";

function coerceAppearancePayload(data: unknown): SiteAppearance {
  return data as SiteAppearance;
}

export async function getPublicSiteAppearance(signal?: AbortSignal): Promise<SiteAppearance> {
  const { data } = await api.get<unknown>(PUBLIC_BASE, { signal });
  return coerceAppearancePayload(data);
}

export async function getAdminSiteAppearance(signal?: AbortSignal): Promise<SiteAppearance> {
  const { data } = await api.get<unknown>(ADMIN_BASE, { signal });
  return coerceAppearancePayload(data);
}

export async function updateSiteAppearance(
  request: UpdateSiteAppearanceRequest
): Promise<SiteAppearance> {
  const { data } = await api.patch<SiteAppearance>(ADMIN_BASE, request);
  return data;
}
