export type MenuItemType = "group" | "link" | "route" | "page" | "action" | "separator" | "template";

export type MenuItemConfig = Record<string, unknown>;

export type MenuItem = {
  id: string;
  menuId: string;
  parentId: string | null;
  sortOrder: number;
  displayName: string;
  icon: string | null;
  itemType: MenuItemType;
  config: MenuItemConfig;
  permissionRequired: string | null;
  isVisible: boolean;
  isSystem: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  children: MenuItem[];
};

export type Menu = {
  id: string;
  key: string;
  name: string;
  description: string | null;
  isSystem: boolean;
  createdAtUtc: string;
  createdBy: string;
  updatedAtUtc: string;
  updatedBy: string;
  items: MenuItem[];
};

export type CreateMenuRequest = {
  key: string;
  name: string;
  description?: string | null;
};

export type UpdateMenuRequest = {
  name?: string | null;
  description?: string | null;
};

export type CreateMenuItemRequest = {
  parentId?: string | null;
  sortOrder?: number;
  displayName: string;
  icon?: string | null;
  itemType: MenuItemType;
  config: MenuItemConfig;
  permissionRequired?: string | null;
  isVisible?: boolean;
};

export type UpdateMenuItemRequest = {
  parentId?: string | null;
  sortOrder?: number | null;
  displayName?: string | null;
  icon?: string | null;
  itemType?: MenuItemType | null;
  config?: MenuItemConfig | null;
  permissionRequired?: string | null;
  isVisible?: boolean | null;
  clearIcon?: boolean;
  clearPermissionRequired?: boolean;
};

export type TreeNode = {
  id: string;
  parentId: string | null;
  sortOrder: number;
};

export type ReplaceTreeRequest = {
  nodes: TreeNode[];
};

export type PageRegistryEntry = {
  id: string;
  path: string;
  contentType: string;
};

export type PageContent = {
  id: string;
  path: string;
  content: string;
  contentType: string;
};
