import { useEffect, useMemo, useState } from "react";
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
import "./PagesMenus.css";

export default function PagesMenus() {
  const { data: menus = [], isLoading } = useMenus();
  const [activeKey, setActiveKey] = useState<string | null>(null);

  useEffect(() => {
    if (!activeKey && menus.length > 0) {
      setActiveKey(menus[0].key);
    }
  }, [menus, activeKey]);

  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">Pages / Menus</h1>
        <p className="page-head-copy">
          Manage every navigation surface on the site. Each tab is a menu — drag items
          to reorder, edit them to change icons, names, or destinations, and add new
          items including custom <strong>page</strong> items that create their own routes.
        </p>
      </div>

      {isLoading && <div>Loading…</div>}

      {!isLoading && menus.length > 0 && (
        <>
          <ul className="nav nav-tabs mb-3">
            {menus.map((m) => (
              <li className="nav-item" key={m.key}>
                <button
                  type="button"
                  className={`nav-link ${activeKey === m.key ? "active" : ""}`}
                  onClick={() => setActiveKey(m.key)}
                >
                  {m.name}
                  {m.isSystem && <i className="fa fa-lock ms-2 text-muted" title="System menu" />}
                </button>
              </li>
            ))}
            <li className="nav-item ms-auto">
              <NewMenuButton />
            </li>
          </ul>

          {activeKey && <MenuPanel key={activeKey} menuKey={activeKey} />}
        </>
      )}
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

  // Snapshot stays "saved" until the user reorders something locally.
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

  if (isLoading || !menu) return <div>Loading…</div>;

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
    <div className="row g-3">
      <div className="col-lg-12">
        <div className="panel panel-inverse">
          <div className="panel-heading d-flex justify-content-between align-items-center">
            <h4 className="panel-title mb-0">{menu.name}</h4>
            <div className="d-flex gap-2">
              {dirty && isStructurallyDirty && (
                <>
                  <button
                    type="button"
                    className="btn btn-sm btn-outline-secondary"
                    onClick={handleCancel}
                  >
                    Cancel
                  </button>
                  <button
                    type="button"
                    className="btn btn-sm btn-primary"
                    onClick={handleSave}
                    disabled={replaceTree.isPending}
                  >
                    Save order
                  </button>
                </>
              )}
              {!menu.isSystem && (
                <button
                  type="button"
                  className="btn btn-sm btn-outline-danger"
                  onClick={() => {
                    if (confirm(`Delete menu '${menu.name}'? This removes all items.`)) {
                      void deleteMenu.mutateAsync(menu.id);
                    }
                  }}
                >
                  Delete menu
                </button>
              )}
            </div>
          </div>
          <div className="panel-body">
            {error && <div className="alert alert-danger">{error}</div>}

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
          </div>
        </div>
      </div>
    </div>
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
    <form className="row g-2 mb-3" onSubmit={submit}>
      <div className="col-md-4">
        <label className="form-label small">Name</label>
        <input className="form-control" value={name} onChange={(e) => setName(e.target.value)} />
      </div>
      <div className="col-md-7">
        <label className="form-label small">Description</label>
        <input
          className="form-control"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
        />
      </div>
      <div className="col-md-1 d-flex align-items-end">
        <button
          type="submit"
          className="btn btn-outline-primary w-100"
          disabled={!isDirty || saving}
        >
          Save
        </button>
      </div>
    </form>
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
      <button
        type="button"
        className="btn btn-sm btn-outline-primary"
        onClick={() => setAdding(true)}
      >
        <i className="fa fa-plus me-1" />
        New menu
      </button>
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
    <form className="d-flex gap-1" onSubmit={submit}>
      <input
        className="form-control form-control-sm font-monospace"
        placeholder="key"
        value={key}
        onChange={(e) => setKey(e.target.value)}
      />
      <input
        className="form-control form-control-sm"
        placeholder="Display name"
        value={name}
        onChange={(e) => setName(e.target.value)}
      />
      <button type="submit" className="btn btn-sm btn-primary" disabled={!key || !name}>
        Add
      </button>
      <button
        type="button"
        className="btn btn-sm btn-link"
        onClick={() => {
          setAdding(false);
          setError(null);
        }}
      >
        Cancel
      </button>
      {error && <span className="text-danger small ms-2">{error}</span>}
    </form>
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
