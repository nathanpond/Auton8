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
import { ActionIcon, Badge, Button, Group, Text, Tooltip, UnstyledButton } from "@mantine/core";
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

  const toggleVisible = async (id: string) => {
    const previous = items.find((i) => i.id === id);
    if (!previous) return;
    const nextVisible = !previous.isVisible;
    setItems((prev) =>
      prev.map((item) => (item.id === id ? { ...item, isVisible: nextVisible } : item))
    );
    await onEditItem(id, { isVisible: nextVisible });
  };

  const applyEdit = async (next: MenuItem, options?: { keepOpen?: boolean }) => {
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
    if (!options?.keepOpen) setEditing(null);
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
      <Group justify="space-between" align="center" mb="xs">
        <Text size="sm" c="dimmed">
          Drag rows to reorder. Drag right or left to change nesting.
        </Text>
        <Group gap="xs">
          <Button
            size="xs"
            variant="default"
            onClick={() => onAddRoot("separator")}
            title="Add a divider line (vertical menus only)"
            leftSection={<i className="fa fa-grip-lines" />}
          >
            Add separator
          </Button>
          <Button
            size="xs"
            variant="outline"
            onClick={() => onAddRoot()}
            leftSection={<i className="fa fa-plus" />}
          >
            Add top-level item
          </Button>
        </Group>
      </Group>

      <DndContext
        sensors={sensors}
        onDragStart={handleDragStart}
        onDragMove={handleDragMove}
        onDragOver={handleDragOver}
        onDragEnd={handleDragEnd}
        onDragCancel={handleDragCancel}
      >
        <SortableContext items={visibleIds} strategy={verticalListSortingStrategy}>
          <ul
            className="menu-tree-list"
            style={{ listStyle: "none", margin: 0, padding: 0 }}
          >
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
                  onToggleVisible={() => {
                    void toggleVisible(item.id);
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
  onToggleVisible: () => void;
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
  onToggleVisible,
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

  // Keep paddingLeft out of `rowStyle` so the computed depth-based paddingLeft
  // on `style` (which is spread second) isn't overridden by a `padding`
  // shorthand collapsing all rows to the same indent.
  const rowStyle: React.CSSProperties = {
    display: "flex",
    alignItems: "center",
    gap: 8,
    paddingTop: "0.5rem",
    paddingBottom: "0.5rem",
    paddingRight: "0.75rem",
    border: "1px solid var(--mantine-color-default-border)",
    borderTop: 0,
    background: isSelected ? "var(--mantine-primary-color-filled)" : "var(--mantine-color-body)",
    color: isSelected ? "var(--mantine-primary-color-contrast)" : undefined,
    cursor: "pointer"
  };

  if (isSeparator) {
    return (
      <li
        ref={setNodeRef}
        style={{ ...rowStyle, ...style }}
        className={`menu-tree-separator${isSelected ? " active" : ""}`}
        onClick={onSelect}
      >
        <UnstyledButton
          ref={setActivatorNodeRef}
          title="Drag to move"
          {...attributes}
          {...listeners}
          onClick={(e) => e.stopPropagation()}
          style={{ color: "var(--mantine-color-dimmed)" }}
        >
          <i className="fa fa-grip-vertical" />
        </UnstyledButton>
        <hr style={{ flex: 1, margin: 0 }} />
        <UnstyledButton
          onClick={(e) => {
            e.stopPropagation();
            onSelect();
          }}
          aria-current={isSelected}
          aria-label="Select separator"
        >
          <Badge color="gray" variant="light" tt="uppercase" style={{ cursor: "pointer" }}>
            separator
          </Badge>
        </UnstyledButton>
        <Tooltip
          label={item.isVisible ? "Visible — click to hide" : "Hidden — click to show"}
          withArrow
        >
          <ActionIcon
            size="sm"
            variant="subtle"
            color={item.isVisible ? "gray" : "yellow"}
            aria-pressed={item.isVisible}
            aria-label="Toggle visibility"
            onClick={(e) => {
              e.stopPropagation();
              onToggleVisible();
            }}
          >
            <i className={`fa ${item.isVisible ? "fa-eye" : "fa-eye-slash"}`} />
          </ActionIcon>
        </Tooltip>
        <UnstyledButton
          title="Delete"
          aria-label="Delete item"
          onClick={(e) => {
            e.stopPropagation();
            onDelete();
          }}
          style={{ color: "var(--mantine-color-red-filled)" }}
        >
          <i className="fa fa-xmark" />
        </UnstyledButton>
      </li>
    );
  }

  return (
    <li
      ref={setNodeRef}
      style={{ ...style, ...rowStyle }}
      className={isSelected ? "active" : undefined}
      onClick={onSelect}
    >
      <UnstyledButton
        ref={setActivatorNodeRef}
        title="Drag to move"
        {...attributes}
        {...listeners}
        onClick={(e) => e.stopPropagation()}
        style={{ color: "var(--mantine-color-dimmed)" }}
      >
        <i className="fa fa-grip-vertical" />
      </UnstyledButton>
      <UnstyledButton
        style={{ visibility: childCount > 0 ? "visible" : "hidden" }}
        onClick={(e) => {
          e.stopPropagation();
          onToggleCollapsed();
        }}
        aria-label={isCollapsed ? "Expand" : "Collapse"}
      >
        <i className={`fa fa-chevron-${isCollapsed ? "right" : "down"}`} />
      </UnstyledButton>
      {item.icon && <i className={item.icon} style={{ color: "var(--mantine-color-dimmed)" }} />}
      <UnstyledButton
        style={{ flex: 1, textAlign: "left", color: "inherit" }}
        onClick={(e) => {
          e.stopPropagation();
          onSelect();
        }}
        aria-current={isSelected}
      >
        {item.displayName}
        {!item.isVisible && (
          <Badge color="yellow" variant="filled" ml={8}>
            hidden
          </Badge>
        )}
      </UnstyledButton>
      <Badge color="gray" variant="light" tt="uppercase">
        {item.itemType}
      </Badge>
      <Tooltip
        label={item.isVisible ? "Visible — click to hide" : "Hidden — click to show"}
        withArrow
      >
        <ActionIcon
          size="sm"
          variant="subtle"
          color={item.isVisible ? "gray" : "yellow"}
          aria-pressed={item.isVisible}
          aria-label="Toggle visibility"
          onClick={(e) => {
            e.stopPropagation();
            onToggleVisible();
          }}
        >
          <i className={`fa ${item.isVisible ? "fa-eye" : "fa-eye-slash"}`} />
        </ActionIcon>
      </Tooltip>
      <Tooltip label="Edit" withArrow>
        <ActionIcon
          size="sm"
          variant="subtle"
          color="gray"
          aria-label="Edit item"
          onClick={(e) => {
            e.stopPropagation();
            onEdit();
          }}
        >
          <i className="fa fa-pen" />
        </ActionIcon>
      </Tooltip>
      <Tooltip label="Delete" withArrow>
        <ActionIcon
          size="sm"
          variant="subtle"
          color="red"
          aria-label="Delete item"
          onClick={(e) => {
            e.stopPropagation();
            onDelete();
          }}
        >
          <i className="fa fa-trash" />
        </ActionIcon>
      </Tooltip>
    </li>
  );
}

function DragPreview({ item }: { item: FlatMenuItem }) {
  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: 8,
        padding: "0.5rem 0.75rem",
        border: "1px solid var(--mantine-color-default-border)",
        background: "var(--mantine-color-body)",
        boxShadow: "var(--mantine-shadow-md)"
      }}
    >
      {item.icon && <i className={item.icon} style={{ color: "var(--mantine-color-dimmed)" }} />}
      <span>{item.displayName}</span>
      <Badge color="gray" variant="light" tt="uppercase" style={{ marginLeft: "auto" }}>
        {item.itemType}
      </Badge>
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
