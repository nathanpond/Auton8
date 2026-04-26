import { useEffect, useMemo, useState } from "react";
import {
  DndContext,
  DragEndEvent,
  DragMoveEvent,
  DragOverEvent,
  DragOverlay,
  DragStartEvent,
  PointerSensor,
  useSensor,
  useSensors
} from "@dnd-kit/core";
import {
  SortableContext,
  useSortable,
  verticalListSortingStrategy
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { Menu, MenuItem, MenuItemType, UpdateMenuItemRequest } from "@/types/menus";
import {
  FlatMenuItem,
  arrayMove,
  flattenTree,
  getDescendantIds,
  getProjection,
  reindex
} from "./menuTreeUtils";
import MenuItemEditModal from "./MenuItemEditModal";

const INDENT_PX = 28;

type Props = {
  menu: Menu;
  onChange: (items: FlatMenuItem[]) => void;
  onAddRoot: (itemType?: MenuItemType) => void;
  onDelete: (id: string) => void;
  onEditItem: (id: string, request: UpdateMenuItemRequest) => Promise<void>;
  selectedId: string | null;
  onSelect: (id: string | null) => void;
};

export default function MenuTreeEditor({
  menu,
  onChange,
  onAddRoot,
  onDelete,
  onEditItem,
  selectedId,
  onSelect
}: Props) {
  // Working copy of the flat tree, mutable in this component.
  const [items, setItems] = useState<FlatMenuItem[]>(() => reindex(flattenTree(menu)));
  const [activeId, setActiveId] = useState<string | null>(null);
  const [overId, setOverId] = useState<string | null>(null);
  const [offsetLeft, setOffsetLeft] = useState(0);
  const [editing, setEditing] = useState<MenuItem | null>(null);
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }));

  // Re-sync the working copy when the parent menu prop changes (e.g. after a
  // server save invalidates the query and refetches).
  useEffect(() => {
    setItems(reindex(flattenTree(menu)));
  }, [menu]);

  const projected =
    activeId && overId
      ? getProjection(items, activeId, overId, offsetLeft, INDENT_PX)
      : null;

  // Hide descendants of the dragged node and any collapsed branches.
  const visibleItems = useMemo(() => {
    const hidden = new Set<string>();
    if (activeId) for (const id of getDescendantIds(items, activeId)) hidden.add(id);
    for (const item of items) {
      if (collapsed.has(item.id)) {
        for (const id of getDescendantIds(items, item.id)) hidden.add(id);
      }
    }
    return items.filter((i) => !hidden.has(i.id));
  }, [items, activeId, collapsed]);

  const visibleIds = visibleItems.map((i) => i.id);
  const activeItem = activeId ? items.find((i) => i.id === activeId) ?? null : null;

  const handleDragStart = ({ active }: DragStartEvent) => {
    setActiveId(String(active.id));
    setOverId(String(active.id));
    onSelect(String(active.id));
  };

  const handleDragMove = ({ delta }: DragMoveEvent) => setOffsetLeft(delta.x);

  const handleDragOver = ({ over }: DragOverEvent) =>
    setOverId(over ? String(over.id) : null);

  const handleDragEnd = ({ active, over }: DragEndEvent) => {
    resetDragState();
    if (!over || !projected) return;
    const activeIndex = items.findIndex((i) => i.id === active.id);
    const overIndex = items.findIndex((i) => i.id === over.id);
    if (activeIndex === -1) return;

    const moved = arrayMove(items, activeIndex, overIndex);
    moved[overIndex] = {
      ...moved[overIndex],
      parentId: projected.parentId,
      depth: projected.depth
    };

    // Re-derive depth for descendants of the moved item so children follow.
    const next = applyDescendantDepths(moved, String(active.id));
    const reindexed = reindex(next);
    setItems(reindexed);
    onChange(reindexed);
  };

  const handleDragCancel = () => resetDragState();

  const resetDragState = () => {
    setActiveId(null);
    setOverId(null);
    setOffsetLeft(0);
  };

  const toggleCollapsed = (id: string) =>
    setCollapsed((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const applyEdit = async (next: MenuItem) => {
    const previous = items.find((i) => i.id === next.id);
    setItems((prev) =>
      prev.map((item) =>
        item.id === next.id
          ? {
              ...item,
              displayName: next.displayName,
              icon: next.icon,
              itemType: next.itemType,
              config: next.config,
              permissionRequired: next.permissionRequired,
              isVisible: next.isVisible
            }
          : item
      )
    );
    setEditing(null);
    await onEditItem(next.id, {
      displayName: next.displayName !== previous?.displayName ? next.displayName : null,
      icon: next.icon ?? null,
      clearIcon: next.icon === null,
      itemType: next.itemType !== previous?.itemType ? next.itemType : null,
      config: next.config,
      permissionRequired: next.permissionRequired,
      clearPermissionRequired: next.permissionRequired === null,
      isVisible: next.isVisible !== previous?.isVisible ? next.isVisible : null
    });
  };

  return (
    <div className="menu-tree-editor">
      <div className="d-flex justify-content-between align-items-center mb-2">
        <small className="text-muted">
          Drag rows to reorder. Drag right or left to change nesting.
        </small>
        <div className="d-flex gap-2">
          <button
            type="button"
            className="btn btn-sm btn-outline-secondary"
            onClick={() => onAddRoot("separator")}
            title="Add a divider line (vertical menus only)"
          >
            <i className="fa fa-grip-lines me-1" /> Add separator
          </button>
          <button
            type="button"
            className="btn btn-sm btn-outline-primary"
            onClick={() => onAddRoot()}
          >
            <i className="fa fa-plus me-1" /> Add top-level item
          </button>
        </div>
      </div>

      <DndContext
        sensors={sensors}
        onDragStart={handleDragStart}
        onDragMove={handleDragMove}
        onDragOver={handleDragOver}
        onDragEnd={handleDragEnd}
        onDragCancel={handleDragCancel}
      >
        <SortableContext items={visibleIds} strategy={verticalListSortingStrategy}>
          <ul className="list-group menu-tree-list">
            {visibleItems.map((item) => {
              const childCount = items.filter((i) => i.parentId === item.id).length;
              const isCollapsed = collapsed.has(item.id);
              const depth =
                activeId === item.id && projected ? projected.depth : item.depth;
              return (
                <SortableRow
                  key={item.id}
                  item={item}
                  depth={depth}
                  childCount={childCount}
                  isCollapsed={isCollapsed}
                  isSelected={selectedId === item.id}
                  onToggleCollapsed={() => toggleCollapsed(item.id)}
                  onSelect={() => onSelect(item.id)}
                  onEdit={() => {
                    const fresh = items.find((i) => i.id === item.id);
                    if (fresh) setEditing(toMenuItem(fresh, items));
                  }}
                  onDelete={() => {
                    if (confirm(`Delete '${item.displayName}' and any children?`)) {
                      const ids = new Set([item.id, ...getDescendantIds(items, item.id)]);
                      const next = reindex(items.filter((i) => !ids.has(i.id)));
                      setItems(next);
                      onChange(next);
                      onDelete(item.id);
                    }
                  }}
                />
              );
            })}
          </ul>
        </SortableContext>
        <DragOverlay>
          {activeItem ? <DragPreview item={activeItem} /> : null}
        </DragOverlay>
      </DndContext>

      {editing && (
        <MenuItemEditModal
          item={editing}
          onSave={applyEdit}
          onCancel={() => setEditing(null)}
        />
      )}
    </div>
  );
}

type RowProps = {
  item: FlatMenuItem;
  depth: number;
  childCount: number;
  isCollapsed: boolean;
  isSelected: boolean;
  onToggleCollapsed: () => void;
  onSelect: () => void;
  onEdit: () => void;
  onDelete: () => void;
};

function SortableRow({
  item,
  depth,
  childCount,
  isCollapsed,
  isSelected,
  onToggleCollapsed,
  onSelect,
  onEdit,
  onDelete
}: RowProps) {
  const {
    attributes,
    listeners,
    setNodeRef,
    setActivatorNodeRef,
    transform,
    transition,
    isDragging
  } = useSortable({ id: item.id });

  const isSeparator = item.itemType === "separator";
  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    paddingLeft: `${depth * INDENT_PX + 8}px`,
    paddingTop: isSeparator ? "0.15rem" : undefined,
    paddingBottom: isSeparator ? "0.15rem" : undefined,
    opacity: isDragging ? 0.5 : 1
  };

  if (isSeparator) {
    return (
      <li
        ref={setNodeRef}
        style={style}
        className={`list-group-item d-flex align-items-center gap-2 menu-tree-separator ${
          isSelected ? "active" : ""
        }`}
        onClick={onSelect}
      >
        <button
          ref={setActivatorNodeRef}
          type="button"
          className="btn btn-sm btn-link p-0 text-secondary"
          title="Drag to move"
          {...attributes}
          {...listeners}
          onClick={(e) => e.stopPropagation()}
        >
          <i className="fa fa-grip-vertical" />
        </button>
        <hr className="flex-grow-1 my-0" />
        <span className="badge bg-light text-dark text-uppercase">separator</span>
        {!item.isSystem && (
          <button
            type="button"
            className="btn btn-sm btn-link p-0 text-danger"
            title="Delete"
            onClick={(e) => {
              e.stopPropagation();
              onDelete();
            }}
          >
            <i className="fa fa-xmark" />
          </button>
        )}
      </li>
    );
  }

  return (
    <li
      ref={setNodeRef}
      style={style}
      className={`list-group-item d-flex align-items-center gap-2 ${
        isSelected ? "active" : ""
      }`}
      onClick={onSelect}
    >
      <button
        ref={setActivatorNodeRef}
        type="button"
        className="btn btn-sm btn-link p-0 text-secondary"
        title="Drag to move"
        {...attributes}
        {...listeners}
        onClick={(e) => e.stopPropagation()}
      >
        <i className="fa fa-grip-vertical" />
      </button>
      <button
        type="button"
        className="btn btn-sm btn-link p-0"
        style={{ visibility: childCount > 0 ? "visible" : "hidden" }}
        onClick={(e) => {
          e.stopPropagation();
          onToggleCollapsed();
        }}
        aria-label={isCollapsed ? "Expand" : "Collapse"}
      >
        <i className={`fa fa-chevron-${isCollapsed ? "right" : "down"}`} />
      </button>
      {item.icon && <i className={`${item.icon} text-secondary`} />}
      <span className="flex-grow-1">
        {item.displayName}
        {!item.isVisible && <span className="badge bg-warning text-dark ms-2">hidden</span>}
        {item.isSystem && <span className="badge bg-secondary ms-2">system</span>}
      </span>
      <span className="badge bg-light text-dark text-uppercase">{item.itemType}</span>
      <button
        type="button"
        className="btn btn-sm btn-outline-secondary"
        onClick={(e) => {
          e.stopPropagation();
          onEdit();
        }}
      >
        <i className="fa fa-pen" />
      </button>
      {!item.isSystem && (
        <button
          type="button"
          className="btn btn-sm btn-outline-danger"
          onClick={(e) => {
            e.stopPropagation();
            onDelete();
          }}
        >
          <i className="fa fa-trash" />
        </button>
      )}
    </li>
  );
}

function DragPreview({ item }: { item: FlatMenuItem }) {
  return (
    <div className="list-group-item d-flex align-items-center gap-2 shadow">
      {item.icon && <i className={`${item.icon} text-secondary`} />}
      <span>{item.displayName}</span>
      <span className="badge bg-light text-dark text-uppercase ms-auto">{item.itemType}</span>
    </div>
  );
}

function toMenuItem(flat: FlatMenuItem, all: FlatMenuItem[]): MenuItem {
  const children = all
    .filter((i) => i.parentId === flat.id)
    .map((c) => toMenuItem(c, all));
  // Strip `depth`/`parentId` from the flat shape to a MenuItem.
  return {
    id: flat.id,
    menuId: flat.menuId,
    parentId: flat.parentId,
    sortOrder: flat.sortOrder,
    displayName: flat.displayName,
    icon: flat.icon,
    itemType: flat.itemType as MenuItemType,
    config: flat.config,
    permissionRequired: flat.permissionRequired,
    isVisible: flat.isVisible,
    isSystem: flat.isSystem,
    createdAtUtc: flat.createdAtUtc,
    updatedAtUtc: flat.updatedAtUtc,
    children
  };
}

function applyDescendantDepths(items: FlatMenuItem[], movedId: string): FlatMenuItem[] {
  // After we mutate the moved row's depth and parentId, walk descendants and
  // recompute their depth = parent.depth + 1 so they stay anchored beneath.
  const byId = new Map(items.map((i) => [i.id, i] as const));
  const fix = (id: string, depth: number) => {
    const node = byId.get(id);
    if (!node) return;
    node.depth = depth;
    for (const child of items.filter((i) => i.parentId === id)) {
      fix(child.id, depth + 1);
    }
  };
  for (const child of items.filter((i) => i.parentId === movedId)) {
    const moved = byId.get(movedId)!;
    fix(child.id, moved.depth + 1);
  }
  return items.slice();
}
