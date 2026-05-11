import { useEffect, useMemo, useState } from "react";
import {
  Alert,
  Box,
  Button,
  Card,
  Grid,
  Group,
  Tabs,
  Text,
  TextInput,
  Title
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import {
  useAdminMenu,
  useCreateMenu,
  useCreateMenuItem,
  useDeleteMenu,
  useDeleteMenuItem,
  useMenus,
  useReplaceMenuTree,
  useUpdateMenu
} from "@/hooks/useMenus";
import { Menu } from "@/types/menus";
import MenuTreeEditor from "./MenuTreeEditor";
import { FlatMenuItem } from "./menuTreeUtils";
import { useUpdateMenuItem } from "@/hooks/useMenus";
import { MenuItemType, UpdateMenuItemRequest } from "@/types/menus";
import PagesMenusHelpModal from "./PagesMenusHelpModal";
import "./PagesMenus.css";

// Tab order for the seeded system menus. Anything else (admin-created menus)
// appears after these in alphabetical order.
const SYSTEM_MENU_ORDER = ["main", "icon", "user", "site-config", "standalone"];

export default function PagesMenus() {
  const { data: menus = [], isLoading } = useMenus();
  const [activeKey, setActiveKey] = useState<string | null>(null);
  const [helpOpen, setHelpOpen] = useState(false);

  const orderedMenus = useMemo(() => {
    const orderIndex = (key: string) => {
      const idx = SYSTEM_MENU_ORDER.indexOf(key);
      return idx === -1 ? Number.MAX_SAFE_INTEGER : idx;
    };
    return [...menus].sort((a, b) => {
      const ai = orderIndex(a.key);
      const bi = orderIndex(b.key);
      if (ai !== bi) return ai - bi;
      return a.name.localeCompare(b.name);
    });
  }, [menus]);

  useEffect(() => {
    if (!activeKey && orderedMenus.length > 0) {
      setActiveKey(orderedMenus[0].key);
    }
  }, [orderedMenus, activeKey]);

  return (
    <>
      <PageHeader
        title="Pages / Menus"
        description={
          <>
            Manage every navigation surface on the site. Each tab is a menu — drag items to
            reorder, edit them to change icons, names, or destinations, and add new items
            including custom <strong>page</strong> items that create their own routes.
          </>
        }
        actions={
          <Button
            variant="subtle"
            size="compact-sm"
            leftSection={<i className="fa fa-circle-question" />}
            onClick={() => setHelpOpen(true)}
            title="How Pages / Menus works"
            aria-label="How Pages / Menus works"
          >
            Help
          </Button>
        }
      />

      {isLoading && <Text>Loading…</Text>}

      {!isLoading && orderedMenus.length > 0 && (
        <>
          <Tabs value={activeKey} onChange={setActiveKey}>
            <Group justify="space-between" align="flex-end" mb="md" wrap="nowrap">
              <Tabs.List style={{ flex: 1 }}>
                {orderedMenus.map((m) => (
                  <Tabs.Tab
                    key={m.key}
                    value={m.key}
                    rightSection={
                      m.isSystem ? (
                        <i
                          className="fa fa-lock"
                          title="System menu"
                          style={{ color: "var(--mantine-color-dimmed)" }}
                        />
                      ) : undefined
                    }
                  >
                    {m.name}
                  </Tabs.Tab>
                ))}
              </Tabs.List>
              <NewMenuButton />
            </Group>
          </Tabs>

          {activeKey && <MenuPanel key={activeKey} menuKey={activeKey} />}
        </>
      )}

      {helpOpen && <PagesMenusHelpModal onClose={() => setHelpOpen(false)} />}
    </>
  );
}

function MenuPanel({ menuKey }: { menuKey: string }) {
  const { data: menu, isLoading } = useAdminMenu(menuKey);
  const [pendingItems, setPendingItems] = useState<FlatMenuItem[] | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const replaceTree = useReplaceMenuTree(menuKey);
  const createItem = useCreateMenuItem(menuKey);
  const deleteItem = useDeleteMenuItem(menuKey);
  const updateItem = useUpdateMenuItem(menuKey);
  const updateMenu = useUpdateMenu(menu?.id ?? "");
  const deleteMenu = useDeleteMenu();

  const dirty = pendingItems !== null;

  const isStructurallyDirty = useMemo(() => {
    if (!pendingItems || !menu) return false;
    const original = flattenForCompare(menu);
    const current = pendingItems.map(({ id, parentId, sortOrder }) => ({
      id,
      parentId,
      sortOrder
    }));
    return JSON.stringify(original) !== JSON.stringify(current);
  }, [pendingItems, menu]);

  if (isLoading || !menu) return <Text>Loading…</Text>;

  const handleAddRoot = async (itemType: MenuItemType = "group") => {
    setError(null);
    try {
      await createItem.mutateAsync({
        displayName: itemType === "separator" ? "" : "New item",
        icon: null,
        itemType,
        config: {},
        isVisible: true
      });
    } catch (err) {
      setError(describeError(err));
    }
  };

  const handleSave = async () => {
    if (!pendingItems) return;
    setError(null);
    try {
      await replaceTree.mutateAsync({
        nodes: pendingItems.map(({ id, parentId, sortOrder }) => ({
          id,
          parentId,
          sortOrder
        }))
      });
      setPendingItems(null);
    } catch (err) {
      setError(describeError(err));
    }
  };

  const handleEditItem = async (id: string, request: UpdateMenuItemRequest) => {
    setError(null);
    try {
      await updateItem.mutateAsync({ id, request });
    } catch (err) {
      setError(describeError(err));
    }
  };

  const handleCancel = () => {
    setPendingItems(null);
    setError(null);
  };

  return (
    <Card withBorder shadow="sm">
      <Group justify="space-between" align="center" mb="md">
        <Title order={5} m={0}>
          {menu.name}
        </Title>
        <Group gap="xs">
          {dirty && isStructurallyDirty && (
            <>
              <Button size="xs" variant="default" onClick={handleCancel}>
                Cancel
              </Button>
              <Button size="xs" onClick={handleSave} loading={replaceTree.isPending}>
                Save order
              </Button>
            </>
          )}
          {!menu.isSystem && (
            <Button
              size="xs"
              variant="outline"
              color="red"
              onClick={() => {
                if (confirm(`Delete menu '${menu.name}'? This removes all items.`)) {
                  void deleteMenu.mutateAsync(menu.id);
                }
              }}
            >
              Delete menu
            </Button>
          )}
        </Group>
      </Group>

      {error && (
        <Alert color="red" variant="light" mb="md">
          {error}
        </Alert>
      )}

      {menu.key === "standalone" && (
        <Alert color="blue" variant="light" mb="md">
          <Group gap="xs" align="center">
            <i className="fa fa-circle-info" />
            <Text size="sm">
              Items on the <strong>Standalone</strong> menu are URL-reachable but do not
              appear in any visible navigation menu. Use it to expose page templates that
              should be available by URL without taking up nav space.
            </Text>
          </Group>
        </Alert>
      )}

      <MenuMetaForm menu={menu} onUpdate={(req) => updateMenu.mutateAsync(req)} />

      <MenuTreeEditor
        menu={menu}
        onChange={setPendingItems}
        onAddRoot={handleAddRoot}
        onDelete={(id) => void deleteItem.mutateAsync(id)}
        onEditItem={handleEditItem}
        selectedId={selectedId}
        onSelect={setSelectedId}
      />
    </Card>
  );
}

function MenuMetaForm({
  menu,
  onUpdate
}: {
  menu: Menu;
  onUpdate: (req: { name?: string | null; description?: string | null }) => Promise<unknown>;
}) {
  const [name, setName] = useState(menu.name);
  const [description, setDescription] = useState(menu.description ?? "");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    setName(menu.name);
    setDescription(menu.description ?? "");
  }, [menu]);

  const isDirty = name !== menu.name || description !== (menu.description ?? "");

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    try {
      await onUpdate({
        name: name !== menu.name ? name : undefined,
        description: description !== (menu.description ?? "") ? description : undefined
      });
    } finally {
      setSaving(false);
    }
  };

  return (
    <Box component="form" onSubmit={submit} mb="md">
      <Grid align="flex-end">
        <Grid.Col span={{ base: 12, md: 4 }}>
          <TextInput
            label="Name"
            size="xs"
            value={name}
            onChange={(e) => setName(e.currentTarget.value)}
          />
        </Grid.Col>
        <Grid.Col span={{ base: 12, md: 7 }}>
          <TextInput
            label="Description"
            size="xs"
            value={description}
            onChange={(e) => setDescription(e.currentTarget.value)}
          />
        </Grid.Col>
        <Grid.Col span={{ base: 12, md: 1 }}>
          <Button type="submit" size="xs" fullWidth variant="outline" disabled={!isDirty || saving}>
            Save
          </Button>
        </Grid.Col>
      </Grid>
    </Box>
  );
}

function NewMenuButton() {
  const create = useCreateMenu();
  const [adding, setAdding] = useState(false);
  const [key, setKey] = useState("");
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);

  if (!adding) {
    return (
      <Button
        size="xs"
        variant="outline"
        leftSection={<i className="fa fa-plus" />}
        onClick={() => setAdding(true)}
      >
        New menu
      </Button>
    );
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    try {
      await create.mutateAsync({ key: key.trim(), name: name.trim() });
      setKey("");
      setName("");
      setAdding(false);
    } catch (err) {
      setError(describeError(err));
    }
  };

  return (
    <Box component="form" onSubmit={submit}>
      <Group gap={4} align="flex-end" wrap="nowrap">
        <TextInput
          size="xs"
          placeholder="key"
          value={key}
          onChange={(e) => setKey(e.currentTarget.value)}
          styles={{ input: { fontFamily: "var(--mantine-font-family-monospace)" } }}
        />
        <TextInput
          size="xs"
          placeholder="Display name"
          value={name}
          onChange={(e) => setName(e.currentTarget.value)}
        />
        <Button type="submit" size="xs" disabled={!key || !name}>
          Add
        </Button>
        <Button
          type="button"
          size="xs"
          variant="subtle"
          onClick={() => {
            setAdding(false);
            setError(null);
          }}
        >
          Cancel
        </Button>
        {error && (
          <Text c="red" size="sm" ml={8}>
            {error}
          </Text>
        )}
      </Group>
    </Box>
  );
}

function flattenForCompare(menu: Menu): { id: string; parentId: string | null; sortOrder: number }[] {
  const out: { id: string; parentId: string | null; sortOrder: number }[] = [];
  const walk = (items: Menu["items"], parentId: string | null) => {
    for (const item of items) {
      out.push({ id: item.id, parentId, sortOrder: item.sortOrder });
      walk(item.children, item.id);
    }
  };
  walk(menu.items, null);
  return out;
}

function describeError(err: unknown): string {
  if (typeof err === "object" && err && "response" in err) {
    const resp = (err as { response?: { data?: { error?: string; message?: string } } }).response;
    return resp?.data?.error ?? resp?.data?.message ?? String(err);
  }
  return err instanceof Error ? err.message : String(err);
}
