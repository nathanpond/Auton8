import { api } from "./client";
import {
  CreateMenuItemRequest,
  CreateMenuRequest,
  Menu,
  MenuItem,
  ReplaceTreeRequest,
  UpdateMenuItemRequest,
  UpdateMenuRequest
} from "@/types/menus";

const ADMIN_BASE = "/api/admin/menus";
const PUBLIC_BASE = "/api/menus";

export async function listMenus(signal?: AbortSignal): Promise<Menu[]> {
  const { data } = await api.get<Menu[]>(ADMIN_BASE, { signal });
  return data;
}

export async function getAdminMenu(key: string, signal?: AbortSignal): Promise<Menu | null> {
  try {
    const { data } = await api.get<Menu>(`${ADMIN_BASE}/${key}`, { signal });
    return data;
  } catch (error) {
    if (isNotFound(error)) return null;
    throw error;
  }
}

export async function getPublicMenu(key: string, signal?: AbortSignal): Promise<Menu | null> {
  try {
    const { data } = await api.get<Menu>(`${PUBLIC_BASE}/${key}`, { signal });
    return data;
  } catch (error) {
    if (isNotFound(error)) return null;
    throw error;
  }
}

export async function createMenu(request: CreateMenuRequest): Promise<Menu> {
  const { data } = await api.post<Menu>(ADMIN_BASE, request);
  return data;
}

export async function updateMenu(id: string, request: UpdateMenuRequest): Promise<Menu> {
  const { data } = await api.patch<Menu>(`${ADMIN_BASE}/${id}`, request);
  return data;
}

export async function deleteMenu(id: string): Promise<void> {
  await api.delete(`${ADMIN_BASE}/${id}`);
}

export async function createMenuItem(
  menuKey: string,
  request: CreateMenuItemRequest
): Promise<MenuItem> {
  const { data } = await api.post<MenuItem>(`${ADMIN_BASE}/${menuKey}/items`, request);
  return data;
}

export async function updateMenuItem(
  id: string,
  request: UpdateMenuItemRequest
): Promise<MenuItem> {
  const { data } = await api.patch<MenuItem>(`${ADMIN_BASE}/items/${id}`, request);
  return data;
}

export async function deleteMenuItem(id: string): Promise<void> {
  await api.delete(`${ADMIN_BASE}/items/${id}`);
}

export async function replaceMenuTree(
  menuKey: string,
  request: ReplaceTreeRequest
): Promise<void> {
  await api.put(`${ADMIN_BASE}/${menuKey}/tree`, request);
}

function isNotFound(error: unknown): boolean {
  const response = (error as { response?: { status?: number } } | undefined)?.response;
  return response?.status === 404;
}
