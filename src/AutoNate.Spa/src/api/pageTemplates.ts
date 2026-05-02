import { api } from "./client";

export type PageTemplateInfo = {
  key: string;
  name: string;
  description: string | null;
  thumbnailUrl: string | null;
  category: string | null;
};

const BASE = "/api/page-templates";

export async function listPageTemplates(signal?: AbortSignal): Promise<PageTemplateInfo[]> {
  const { data } = await api.get<PageTemplateInfo[]>(BASE, { signal });
  return data;
}
