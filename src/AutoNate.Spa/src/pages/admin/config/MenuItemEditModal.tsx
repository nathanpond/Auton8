import { useEffect, useState } from "react";
import { MenuItem, MenuItemType } from "@/types/menus";
import IconPicker from "@/components/IconPicker";
import { findIcon } from "@/lib/faIcons";

function extractIconName(stored: string | null | undefined): string {
  if (!stored) return "";
  const tokens = stored.split(/\s+/).filter(Boolean);
  for (const t of tokens) {
    if (findIcon(t)) return t.startsWith("fa-") ? t : `fa-${t}`;
  }
  return stored.trim();
}

type Props = {
  item: MenuItem;
  onSave: (next: MenuItem) => void;
  onCancel: () => void;
};

const ITEM_TYPES: { value: MenuItemType; label: string; description: string }[] = [
  { value: "group", label: "Group", description: "A header that contains child items." },
  { value: "route", label: "Route", description: "Navigates to an existing route in the app." },
  { value: "page", label: "Page", description: "Defines a new route with custom HTML/JSX content." },
  { value: "link", label: "Link", description: "Opens an external URL." },
  { value: "action", label: "Action", description: "Triggers a built-in action (e.g. logout)." },
  { value: "separator", label: "Separator", description: "A divider line between menu items (vertical menus only)." }
];

const DYNAMIC_CHILDREN_OPTIONS = [
  { value: "", label: "(none)" },
  { value: "recordTypes", label: "Record Types — list each active record type" }
];

const ACTION_OPTIONS = [{ value: "logout", label: "Logout (POST /account/logout)" }];

export default function MenuItemEditModal({ item, onSave, onCancel }: Props) {
  const [draft, setDraft] = useState<MenuItem>(item);
  const [iconQuery, setIconQuery] = useState<string>(() => extractIconName(item.icon));

  useEffect(() => {
    setDraft(item);
    setIconQuery(extractIconName(item.icon));
  }, [item]);

  const handleIconChange = (next: string) => {
    setIconQuery(next);
    if (!next.trim()) {
      setDraft((d) => ({ ...d, icon: null }));
      return;
    }
    const found = findIcon(next);
    if (found) {
      setDraft((d) => ({ ...d, icon: `fa fa-${found.name}` }));
    } else {
      setDraft((d) => ({ ...d, icon: null }));
    }
  };

  const config = (draft.config ?? {}) as Record<string, unknown>;
  const setConfigField = (key: string, value: unknown) =>
    setDraft((d) => ({ ...d, config: { ...((d.config ?? {}) as Record<string, unknown>), [key]: value } }));

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    if (draft.itemType !== "separator" && !draft.displayName.trim()) return;
    onSave({ ...draft, displayName: draft.displayName.trim() });
  };

  return (
    <div
      className="modal show d-block"
      tabIndex={-1}
      style={{ background: "rgba(0,0,0,0.5)" }}
      onClick={onCancel}
    >
      <div className="modal-dialog modal-lg" onClick={(e) => e.stopPropagation()}>
        <form className="modal-content" onSubmit={submit}>
          <div className="modal-header">
            <h5 className="modal-title">
              Edit menu item
              {draft.isSystem && <span className="badge bg-secondary ms-2">system</span>}
            </h5>
            <button type="button" className="btn-close" onClick={onCancel} aria-label="Close" />
          </div>

          <div className="modal-body">
            <div className="row g-3">
              {draft.itemType !== "separator" && (
                <div className="col-md-6">
                  <label className="form-label">Display name</label>
                  <input
                    className="form-control"
                    value={draft.displayName}
                    onChange={(e) => setDraft({ ...draft, displayName: e.target.value })}
                    required
                  />
                </div>
              )}
              {draft.itemType !== "separator" && (
                <div className="col-md-6">
                  <label className="form-label">Icon</label>
                  <IconPicker
                    value={iconQuery}
                    onChange={handleIconChange}
                    placeholder="Search icons (e.g. star)"
                  />
                </div>
              )}

              <div className={draft.itemType === "separator" ? "col-12" : "col-md-6"}>
                <label className="form-label">Item type</label>
                <select
                  className="form-select"
                  value={draft.itemType}
                  disabled={draft.isSystem}
                  onChange={(e) => setDraft({ ...draft, itemType: e.target.value as MenuItemType })}
                >
                  {ITEM_TYPES.map((t) => (
                    <option key={t.value} value={t.value}>
                      {t.label}
                    </option>
                  ))}
                </select>
                <small className="text-muted">
                  {ITEM_TYPES.find((t) => t.value === draft.itemType)?.description}
                </small>
              </div>

              {draft.itemType !== "separator" && (
              <div className="col-md-6">
                <label className="form-label">Permission required</label>
                <input
                  className="form-control font-monospace"
                  placeholder="kind.action (e.g. siteconfig.edit)"
                  value={draft.permissionRequired ?? ""}
                  onChange={(e) =>
                    setDraft({ ...draft, permissionRequired: e.target.value || null })
                  }
                />
                <small className="text-muted">
                  Optional — when set, the item is hidden from users without this permission.
                </small>
              </div>
              )}

              {draft.itemType === "group" && (
                <div className="col-12">
                  <label className="form-label">Dynamic children source</label>
                  <select
                    className="form-select"
                    value={String(config.dynamicChildren ?? "")}
                    onChange={(e) =>
                      setConfigField(
                        "dynamicChildren",
                        e.target.value === "" ? undefined : e.target.value
                      )
                    }
                  >
                    {DYNAMIC_CHILDREN_OPTIONS.map((o) => (
                      <option key={o.value} value={o.value}>
                        {o.label}
                      </option>
                    ))}
                  </select>
                  <small className="text-muted">
                    Appends auto-generated children at runtime (e.g. one entry per record type).
                  </small>
                </div>
              )}

              {draft.itemType === "route" && (
                <>
                  <div className="col-md-6">
                    <label className="form-label">Route path (target)</label>
                    <input
                      className="form-control font-monospace"
                      placeholder="/records/CAR"
                      value={String(config.path ?? "")}
                      onChange={(e) => setConfigField("path", e.target.value)}
                    />
                    <small className="text-muted">
                      An existing app route (e.g. <code>/admin/roles</code>).
                    </small>
                  </div>
                  <div className="col-md-6">
                    <label className="form-label">Alias URL (optional)</label>
                    <input
                      className="form-control font-monospace"
                      placeholder="/cars"
                      value={String(config.aliasPath ?? "")}
                      onChange={(e) =>
                        setConfigField(
                          "aliasPath",
                          e.target.value === "" ? undefined : e.target.value
                        )
                      }
                    />
                    <small className="text-muted">
                      If set, the menu links to this URL and renders the target route's
                      content underneath. Example: alias <code>/cars</code> shows the
                      same view as <code>/records/CAR</code>.
                    </small>
                  </div>
                </>
              )}

              {draft.itemType === "link" && (
                <>
                  <div className="col-md-9">
                    <label className="form-label">URL</label>
                    <input
                      className="form-control font-monospace"
                      placeholder="https://example.com"
                      value={String(config.href ?? "")}
                      onChange={(e) => setConfigField("href", e.target.value)}
                    />
                  </div>
                  <div className="col-md-3 d-flex align-items-end">
                    <div className="form-check">
                      <input
                        type="checkbox"
                        className="form-check-input"
                        id="link-new-tab"
                        checked={Boolean(config.openInNewTab)}
                        onChange={(e) => setConfigField("openInNewTab", e.target.checked)}
                      />
                      <label className="form-check-label" htmlFor="link-new-tab">
                        Open in new tab
                      </label>
                    </div>
                  </div>
                </>
              )}

              {draft.itemType === "page" && (
                <>
                  <div className="col-md-8">
                    <label className="form-label">Page path</label>
                    <input
                      className="form-control font-monospace"
                      placeholder="/cars/gallery"
                      value={String(config.path ?? "")}
                      onChange={(e) => setConfigField("path", e.target.value)}
                    />
                    <small className="text-muted">
                      A new app route. Must not collide with built-in routes.
                    </small>
                  </div>
                  <div className="col-md-4">
                    <label className="form-label">Content type</label>
                    <select
                      className="form-select"
                      value={String(config.contentType ?? "html")}
                      onChange={(e) => setConfigField("contentType", e.target.value)}
                    >
                      <option value="html">HTML</option>
                      <option value="jsx">JSX (whitelisted components)</option>
                    </select>
                  </div>
                  <div className="col-12">
                    <label className="form-label">Content</label>
                    <textarea
                      className="form-control font-monospace"
                      rows={10}
                      placeholder='<div className="alert alert-info">Hello world</div>'
                      value={String(config.content ?? "")}
                      onChange={(e) => setConfigField("content", e.target.value)}
                    />
                    <small className="text-muted">
                      JSX content can use plain HTML elements, plus <code>NavLink</code> and{" "}
                      <code>Link</code>. Inline event handlers are not allowed.
                    </small>
                  </div>
                </>
              )}

              {draft.itemType === "action" && (
                <div className="col-12">
                  <label className="form-label">Action</label>
                  <select
                    className="form-select"
                    value={String(config.action ?? "logout")}
                    onChange={(e) => setConfigField("action", e.target.value)}
                  >
                    {ACTION_OPTIONS.map((o) => (
                      <option key={o.value} value={o.value}>
                        {o.label}
                      </option>
                    ))}
                  </select>
                </div>
              )}

              {draft.itemType !== "separator" && (
                <div className="col-12">
                  <div className="form-check">
                    <input
                      type="checkbox"
                      className="form-check-input"
                      id="visible-toggle"
                      checked={draft.isVisible}
                      onChange={(e) => setDraft({ ...draft, isVisible: e.target.checked })}
                    />
                    <label className="form-check-label" htmlFor="visible-toggle">
                      Visible
                    </label>
                  </div>
                </div>
              )}
            </div>
          </div>

          <div className="modal-footer">
            <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>
              Cancel
            </button>
            <button type="submit" className="btn btn-primary">
              Apply
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
