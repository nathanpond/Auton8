import { useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import {
  Plugin,
  deletePlugin,
  disablePlugin,
  enablePlugin,
  listPlugins,
  uploadPlugin,
} from "@/api/plugins";

export const PLUGINS_KEY = ["admin", "plugins"] as const;

// Plugins can register/remove menu items, page templates, and pages as they
// enable/disable/delete (and they get swept on upload-replace too). Invalidate
// every related query so the sidebar, the menu-item editor's template picker,
// and any admin page rendering against the menu tree pick up the new state
// without a hard refresh. Prefix-matching means this catches MENUS_QUERY_KEY
// (`["menus"]`), ADMIN_MENU_QUERY_KEY (`["menus","admin",…]`),
// PUBLIC_MENU_QUERY_KEY (`["menus","public",…]`), and the per-key page caches
// in one call.
function invalidatePluginAndMenus(qc: QueryClient) {
  qc.invalidateQueries({ queryKey: PLUGINS_KEY });
  qc.invalidateQueries({ queryKey: ["menus"] });
  qc.invalidateQueries({ queryKey: ["pages"] });
  qc.invalidateQueries({ queryKey: ["page-templates"] });
}

export function usePlugins() {
  return useQuery<Plugin[]>({
    queryKey: PLUGINS_KEY,
    queryFn: ({ signal }) => listPlugins(signal),
  });
}

export function useUploadPlugin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (file: File) => uploadPlugin(file),
    onSuccess: () => invalidatePluginAndMenus(qc),
  });
}

export function useEnablePlugin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => enablePlugin(id),
    onSuccess: () => invalidatePluginAndMenus(qc),
  });
}

export function useDisablePlugin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => disablePlugin(id),
    onSuccess: () => invalidatePluginAndMenus(qc),
  });
}

export function useDeletePlugin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deletePlugin(id),
    onSuccess: () => invalidatePluginAndMenus(qc),
  });
}
