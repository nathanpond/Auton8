import { MenuItem } from "@/types/menus";

// Compute the URL a menu item links to. Mirrors the backend's path resolution
// in EfCoreMenuStore.ParseRegistryEntry — used by nav surfaces (NavMenu,
// ConfigLayout sidenav) so a templated item's link points at the same URL the
// SPA's catch-all renders it under. Templates do not carry a default URL —
// every template menu item owns its own config.path.
export function resolveItemPath(item: MenuItem): string | null {
  const config = item.config ?? {};
  if (item.itemType === "template") {
    const explicit = typeof config.path === "string" ? (config.path as string) : null;
    return explicit && explicit.length > 0 ? explicit : null;
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
