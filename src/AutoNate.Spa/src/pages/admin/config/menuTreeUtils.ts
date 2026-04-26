import { Menu, MenuItem } from "@/types/menus";

export type FlatMenuItem = Omit<MenuItem, "children"> & {
  depth: number;
  parentId: string | null;
};

export function flattenTree(menu: Menu): FlatMenuItem[] {
  const out: FlatMenuItem[] = [];
  const walk = (items: MenuItem[], depth: number, parentId: string | null) => {
    for (const item of items) {
      const { children, ...rest } = item;
      out.push({ ...rest, depth, parentId });
      if (children.length > 0) walk(children, depth + 1, item.id);
    }
  };
  walk(menu.items, 0, null);
  return out;
}

export function rebuildTree(items: FlatMenuItem[]): MenuItem[] {
  const byId = new Map<string, MenuItem>();
  for (const flat of items) {
    const { depth: _depth, parentId: _parentId, ...rest } = flat;
    byId.set(flat.id, { ...rest, parentId: flat.parentId, children: [] });
  }
  const roots: MenuItem[] = [];
  for (const flat of items) {
    const node = byId.get(flat.id)!;
    if (flat.parentId && byId.has(flat.parentId)) {
      byId.get(flat.parentId)!.children.push(node);
    } else {
      roots.push(node);
    }
  }
  return roots;
}

export type Projected = {
  depth: number;
  maxDepth: number;
  minDepth: number;
  parentId: string | null;
};

// Computes the parent id and depth for an item being dragged, based on the
// drag offset (px) relative to its position in the flattened list. Mirrors the
// dnd-kit Sortable Tree example.
export function getProjection(
  items: FlatMenuItem[],
  activeId: string,
  overId: string,
  dragOffset: number,
  indentationWidth: number
): Projected {
  const overItemIndex = items.findIndex(({ id }) => id === overId);
  const activeItemIndex = items.findIndex(({ id }) => id === activeId);
  const activeItem = items[activeItemIndex];
  const newItems = arrayMove(items, activeItemIndex, overItemIndex);
  const previousItem = newItems[overItemIndex - 1];
  const nextItem = newItems[overItemIndex + 1];
  const dragDepth = Math.round(dragOffset / indentationWidth);
  const projectedDepth = (activeItem?.depth ?? 0) + dragDepth;

  const maxDepth = previousItem ? previousItem.depth + 1 : 0;
  const minDepth = nextItem ? nextItem.depth : 0;
  let depth = projectedDepth;
  if (depth >= maxDepth) depth = maxDepth;
  else if (depth < minDepth) depth = minDepth;

  return { depth, maxDepth, minDepth, parentId: getParentId() };

  function getParentId(): string | null {
    if (depth === 0 || !previousItem) return null;
    if (depth === previousItem.depth) return previousItem.parentId;
    if (depth > previousItem.depth) return previousItem.id;
    const newParent = newItems
      .slice(0, overItemIndex)
      .reverse()
      .find((item) => item.depth === depth)?.parentId;
    return newParent ?? null;
  }
}

export function arrayMove<T>(arr: T[], from: number, to: number): T[] {
  const next = arr.slice();
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved);
  return next;
}

// Returns the ids of all descendants of `id` in a flat list.
export function getDescendantIds(items: FlatMenuItem[], id: string): string[] {
  const out: string[] = [];
  const stack = [id];
  while (stack.length > 0) {
    const current = stack.pop()!;
    for (const item of items) {
      if (item.parentId === current) {
        out.push(item.id);
        stack.push(item.id);
      }
    }
  }
  return out;
}

// Recomputes sort_order in stable order and returns a flat list ready to send
// to the server.
export function reindex(items: FlatMenuItem[]): FlatMenuItem[] {
  const counters = new Map<string, number>();
  return items.map((item) => {
    const key = item.parentId ?? "__root__";
    const n = (counters.get(key) ?? 0);
    counters.set(key, n + 1);
    return { ...item, sortOrder: n };
  });
}
