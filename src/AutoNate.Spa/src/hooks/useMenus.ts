import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  createMenu,
  createMenuItem,
  deleteMenu,
  deleteMenuItem,
  getAdminMenu,
  getPublicMenu,
  listMenus,
  replaceMenuTree,
  updateMenu,
  updateMenuItem
} from "@/api/menus";
import {
  CreateMenuItemRequest,
  CreateMenuRequest,
  Menu,
  MenuItem,
  ReplaceTreeRequest,
  UpdateMenuItemRequest,
  UpdateMenuRequest
} from "@/types/menus";

export const MENUS_QUERY_KEY = ["menus"] as const;
export const ADMIN_MENU_QUERY_KEY = (key: string) => ["menus", "admin", key] as const;
export const PUBLIC_MENU_QUERY_KEY = (key: string) => ["menus", "public", key] as const;

export function useMenus() {
  return useQuery<Menu[]>({
    queryKey: MENUS_QUERY_KEY,
    queryFn: ({ signal }) => listMenus(signal)
  });
}

export function useAdminMenu(key: string | null) {
  return useQuery<Menu | null>({
    queryKey: ADMIN_MENU_QUERY_KEY(key ?? "unset"),
    queryFn: ({ signal }) => (key ? getAdminMenu(key, signal) : Promise.resolve(null)),
    enabled: Boolean(key)
  });
}

export function usePublicMenu(key: string) {
  return useQuery<Menu | null>({
    queryKey: PUBLIC_MENU_QUERY_KEY(key),
    queryFn: ({ signal }) => getPublicMenu(key, signal),
    staleTime: 30_000
  });
}

export function useCreateMenu() {
  const qc = useQueryClient();
  return useMutation<Menu, Error, CreateMenuRequest>({
    mutationFn: createMenu,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: MENUS_QUERY_KEY });
    }
  });
}

export function useUpdateMenu(id: string) {
  const qc = useQueryClient();
  return useMutation<Menu, Error, UpdateMenuRequest>({
    mutationFn: (request) => updateMenu(id, request),
    onSuccess: (menu) => {
      qc.invalidateQueries({ queryKey: MENUS_QUERY_KEY });
      qc.invalidateQueries({ queryKey: ADMIN_MENU_QUERY_KEY(menu.key) });
      qc.invalidateQueries({ queryKey: PUBLIC_MENU_QUERY_KEY(menu.key) });
    }
  });
}

export function useDeleteMenu() {
  const qc = useQueryClient();
  return useMutation<void, Error, string>({
    mutationFn: deleteMenu,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: MENUS_QUERY_KEY });
    }
  });
}

export function useCreateMenuItem(menuKey: string) {
  const qc = useQueryClient();
  return useMutation<MenuItem, Error, CreateMenuItemRequest>({
    mutationFn: (request) => createMenuItem(menuKey, request),
    onSuccess: () => invalidateMenu(qc, menuKey)
  });
}

export function useUpdateMenuItem(menuKey: string) {
  const qc = useQueryClient();
  return useMutation<MenuItem, Error, { id: string; request: UpdateMenuItemRequest }>({
    mutationFn: ({ id, request }) => updateMenuItem(id, request),
    onSuccess: () => invalidateMenu(qc, menuKey)
  });
}

export function useDeleteMenuItem(menuKey: string) {
  const qc = useQueryClient();
  return useMutation<void, Error, string>({
    mutationFn: deleteMenuItem,
    onSuccess: () => invalidateMenu(qc, menuKey)
  });
}

export function useReplaceMenuTree(menuKey: string) {
  const qc = useQueryClient();
  return useMutation<void, Error, ReplaceTreeRequest>({
    mutationFn: (request) => replaceMenuTree(menuKey, request),
    onSuccess: () => invalidateMenu(qc, menuKey)
  });
}

function invalidateMenu(qc: ReturnType<typeof useQueryClient>, menuKey: string) {
  qc.invalidateQueries({ queryKey: ADMIN_MENU_QUERY_KEY(menuKey) });
  qc.invalidateQueries({ queryKey: PUBLIC_MENU_QUERY_KEY(menuKey) });
  qc.invalidateQueries({ queryKey: ["pages"] });
}
