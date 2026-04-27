import { PageTemplateInfo } from "@/api/pageTemplates";
import { MenuItem } from "@/types/menus";

// Compute the URL a menu item links to. Mirrors the backend's path resolution
// in EfCoreMenuStore.ParseRegistryEntry — used by nav surfaces (NavMenu,
// ConfigLayout sidenav) so a templated item's link points at the same URL the
// SPA's catch-all renders it under.
export function resolveItemPath(
  item: MenuItem,
  templates: readonly PageTemplateInfo[] | undefined
): string | null {
  const config = item.config ?? {};
  if (item.itemType === "template") {
    const explicit = typeof config.path === "string" ? (config.path as string) : null;
    if (explicit && explicit.length > 0) return explicit;
    const key = typeof config.templateKey === "string" ? (config.templateKey as string) : null;
    if (!key) return null;
    if (!Array.isArray(templates)) return null;
    return templates.find((t) => t.key === key)?.defaultPath ?? null;
  }
  if (item.itemType === "route") {
    const aliasPath = typeof config.aliasPath === "string" ? (config.aliasPath as string) : null;
    const path = typeof config.path === "string" ? (config.path as string) : null;
    return aliasPath ?? path;
  }
  if (item.itemType === "page") {
    return typeof config.path === "string" ? (config.path as string) : null;
  }
  return null;
}
